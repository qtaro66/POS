using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class ClosingLifecycleWireTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"context-wire-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Two_stage_receipts_recover_without_reclosing_or_mutating_frozen_payloads(bool loseContextAck, bool loseCentralParent)
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        var command = new StoreLifecycleCommand("open", "store", "first", StoreLifecycleAction.OpenStore, 500,
            new("employee", "device", Now, new(2026, 11, 22)));
        await store.ExecuteStoreLifecycleAsync("branch", command);
        await store.ExecuteStoreLifecycleAsync("branch", command with { RequestId = "uncounted", Action = StoreLifecycleAction.CloseShift, CashAmount = null });
        await store.ExecuteStoreLifecycleAsync("branch", command with { RequestId = "next", ShiftId = "second", Action = StoreLifecycleAction.OpenShift, CashAmount = 200 });
        await store.ExecuteStoreLifecycleAsync("branch", command with { RequestId = "close", ShiftId = "second", Action = StoreLifecycleAction.CloseStore, CashAmount = 490 });
        await using var receiver = new Receiver();
        var parentBodies = new List<string>(); var contextBodies = new List<string>();
        var clock = new Clock(); var lost = false;
        using var handler = new Handler(async (request, token) =>
        {
            var context = request.RequestUri!.AbsolutePath == HttpClosingLifecycleTransport.Route;
            var body = await request.Content!.ReadAsStringAsync(token);
            (context ? contextBodies : parentBodies).Add(body);
            var response = await receiver.Call(context, body, token);
            if (context && response.StatusCode == HttpStatusCode.OK && loseContextAck && !lost)
            {
                lost = true; response.Dispose(); throw new HttpRequestException("synthetic lost context acknowledgement");
            }
            return response;
        });
        using var http = new HttpClient(handler);
        var parentTransport = new HttpClosingReportTransport(http, new("https://central.example/api/v1/closing-reports"), TimeSpan.FromSeconds(2), TimeProvider.System);
        var contextTransport = new HttpClosingLifecycleTransport(http, new("https://central.example/api/v1/closing-reports/lifecycle"), TimeSpan.FromSeconds(2), TimeProvider.System);
        var settings = Options.Create(new ClosingReportDeliveryOptions { Enabled = true, LifecycleEnabled = true, BranchId = "branch", RetryDelay = TimeSpan.FromSeconds(10) });
        // Establish both original report receipts first, leaving both companion jobs pending.
        var baseCycle = new ClosingReportDeliveryCycle(store, parentTransport, clock, settings);
        Assert.True(await baseCycle.RunOnceAsync()); Assert.True(await baseCycle.RunOnceAsync());
        var pending = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now);
        Assert.Equal("Pending", pending.State);
        Assert.Equal(0, pending.ClosingReports.PendingCount); Assert.Equal(2, pending.LifecycleReports!.PendingCount);
        var before = await store.ReadClosingReportDetailAsync("branch", "close", Now);
        Assert.Equal("Delivered", before!.Report.DeliveryState); Assert.Equal("Queued", before.LifecycleDelivery!.DeliveryState);
        var frozen = JsonSerializer.Serialize(before.Lifecycle);
        var parentReceipt = before.Report.DeliveredAt;
        if (loseCentralParent) { receiver.Data.Parents.Clear(); }
        var contextCycle = new ClosingLifecycleDeliveryCycle(store, contextTransport, parentTransport, clock, settings);
        Assert.True(await contextCycle.RunOnceAsync());
        if (loseContextAck)
        {
            Assert.Equal("Pending", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", clock.Utc)).State);
            clock.Utc = Now.AddSeconds(10);
            store = new SqliteTableSessionCommandStore(_path);
            contextCycle = new ClosingLifecycleDeliveryCycle(store, contextTransport, parentTransport, clock, settings);
        }
        for (var index = 0; index < 4; index++) { await contextCycle.RunOnceAsync(); }
        var completed = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", clock.Utc);
        Assert.Equal("Current", completed.State); Assert.Equal(2, completed.LifecycleReports!.DeliveredCount);
        Assert.Equal(2, receiver.Data.Contexts.Count); Assert.Equal(2, receiver.Data.Parents.Count);
        var after = await store.ReadClosingReportDetailAsync("branch", "close", clock.Utc);
        Assert.Equal("Delivered", after!.LifecycleDelivery!.DeliveryState);
        Assert.Equal(parentReceipt, after.Report.DeliveredAt); Assert.Equal(1, after.Report.AttemptCount);
        Assert.Equal(frozen, JsonSerializer.Serialize(after.Lifecycle));
        foreach (var group in contextBodies.GroupBy(body => JsonDocument.Parse(body).RootElement.GetProperty("contextId").GetString()))
        {
            Assert.Single(group.Distinct(StringComparer.Ordinal));
        }
        foreach (var group in parentBodies.GroupBy(body => JsonDocument.Parse(body).RootElement.GetProperty("reportId").GetString()))
        {
            Assert.Single(group.Distinct(StringComparer.Ordinal));
            using var document = JsonDocument.Parse(group.First()); Assert.False(document.RootElement.TryGetProperty("lifecycle", out _));
        }
        Assert.Equal(loseCentralParent ? 4 : 2, parentBodies.Count);
        Assert.Equal(loseContextAck ? 3 : loseCentralParent ? 4 : 2, contextBodies.Count);
    }

    // Simulated durable storage is shared across mapped HTTP requests; real PostgreSQL is a staging boundary.
    private sealed class Data : IClosingReportIngestionStore, IClosingReportContextIngestionStore
    {
        internal Dictionary<string, string> Parents { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> Contexts { get; } = new(StringComparer.Ordinal);
        public Task<ClosingReportPersistenceResult> PersistAsync(ClosingReportEnvelope report, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Parents.TryGetValue(report.ReportId, out var saved)) { return Task.FromResult(saved == report.PayloadJson ? ClosingReportPersistenceResult.AlreadyAccepted : ClosingReportPersistenceResult.Conflict); }
            Parents.Add(report.ReportId, report.PayloadJson); return Task.FromResult(ClosingReportPersistenceResult.Accepted);
        }
        public Task<ClosingReportContextPersistenceResult> PersistAsync(ClosingReportContextEnvelope context, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!Parents.TryGetValue(context.ReportId, out var parent)) { return Task.FromResult(ClosingReportContextPersistenceResult.ParentMissing); }
            if (!context.MatchesParent(parent)) { return Task.FromResult(ClosingReportContextPersistenceResult.Conflict); }
            if (Contexts.TryGetValue(context.ContextId, out var saved)) { return Task.FromResult(saved == context.PayloadJson ? ClosingReportContextPersistenceResult.AlreadyAccepted : ClosingReportContextPersistenceResult.Conflict); }
            Contexts.Add(context.ContextId, context.PayloadJson); return Task.FromResult(ClosingReportContextPersistenceResult.Accepted);
        }
    }
    private sealed class Ready : IPostgresClosingReportReadinessProbe, IPostgresClosingReportContextReadinessProbe
    { public Task<bool> IsReadyAsync(CancellationToken token = default) => Task.FromResult(true); }
    private sealed class Receiver : IAsyncDisposable
    {
        internal Data Data { get; } = new(); private readonly WebApplication _app;
        internal Receiver()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<IClosingReportIngestionStore>(Data);
            builder.Services.AddSingleton<IClosingReportContextIngestionStore>(Data);
            builder.Services.AddSingleton<IPostgresClosingReportReadinessProbe, Ready>();
            builder.Services.AddSingleton<IPostgresClosingReportContextReadinessProbe, Ready>();
            _app = builder.Build();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CentralClosingReports:Enabled"] = "true" }).Build();
            _app.MapCentralClosingReportsIfEnabled(config); _app.MapCentralClosingReportContextsIfEnabled(config);
        }
        internal async Task<HttpResponseMessage> Call(bool context, string body, CancellationToken token)
        {
            var name = context ? CentralClosingReportContextEndpoints.EndpointName : CentralClosingReportEndpoints.EndpointName;
            var route = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints).Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
            var request = new DefaultHttpContext
            {
                RequestServices = _app.Services,
                RequestAborted = token,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CentralCloudClaimTypes.BranchId, "branch")], "synthetic-branch-certificate"))
            };
            request.Request.Method = "POST"; request.Request.Scheme = "https"; request.Request.ContentType = "application/json";
            request.Request.Path = context ? CentralClosingReportContextEndpoints.Route : CentralClosingReportEndpoints.Route;
            request.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body)); request.Response.Body = new MemoryStream();
            await route.RequestDelegate!(request);
            request.Response.Body.Position = 0; using var reader = new StreamReader(request.Response.Body);
            return new((HttpStatusCode)request.Response.StatusCode) { Content = new StringContent(await reader.ReadToEndAsync(token), Encoding.UTF8, "application/json") };
        }
        public ValueTask DisposeAsync() => _app.DisposeAsync();
    }
    private sealed class Clock : TimeProvider { internal DateTimeOffset Utc { get; set; } = Now; public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); } }
}
