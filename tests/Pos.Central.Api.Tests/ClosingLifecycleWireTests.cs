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
    [InlineData(true, true)]
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
        Assert.Equal(2 + (loseContextAck ? 1 : 0) + (loseCentralParent ? 2 : 0), contextBodies.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_outage_or_shutdown_after_commit_recovers_from_reopened_queue(bool cancelAfterCommit)
    {
        var store = await ClosedStore();
        await using var receiver = new Receiver();
        var clock = new Clock();
        using var shutdown = new CancellationTokenSource();
        var contextBodies = new List<string>();
        var parentBodies = new List<string>();
        var failOnce = true;
        using var handler = new Handler(async (request, token) =>
        {
            var context = request.RequestUri!.AbsolutePath == HttpClosingLifecycleTransport.Route;
            var body = await request.Content!.ReadAsStringAsync(token);
            (context ? contextBodies : parentBodies).Add(body);
            if (context && failOnce)
            {
                failOnce = false;
                if (cancelAfterCommit)
                {
                    using var accepted = await receiver.Call(true, body, token);
                    Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                    shutdown.Cancel();
                    throw new OperationCanceledException(token);
                }
                throw new HttpRequestException("synthetic Central outage before commit");
            }
            return await receiver.Call(context, body, token);
        });
        using var http = new HttpClient(handler);
        var parent = ParentTransport(http);
        Assert.True(await new ClosingReportDeliveryCycle(store, parent, clock, Settings()).RunOnceAsync());
        var before = (await store.ReadClosingReportDetailAsync("branch", "close", Now))!;
        var cycle = new ClosingLifecycleDeliveryCycle(store, ContextTransport(http), parent, clock, Settings());
        if (cancelAfterCommit)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cycle.RunOnceAsync(shutdown.Token));
        }
        else { Assert.True(await cycle.RunOnceAsync()); }
        var waiting = (await store.ReadClosingReportDetailAsync("branch", "close", Now))!;
        Assert.Equal(cancelAfterCommit ? "InFlight" : "RetryScheduled", waiting.LifecycleDelivery!.DeliveryState);
        Assert.Equal("Pending", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now)).State);
        Assert.Equal(cancelAfterCommit ? 1 : 0, receiver.Data.Contexts.Count);
        clock.Utc = Now.AddMinutes(2);
        store = new SqliteTableSessionCommandStore(_path);
        Assert.True(await new ClosingLifecycleDeliveryCycle(store, ContextTransport(http), parent, clock, Settings()).RunOnceAsync());
        Assert.False(await new ClosingLifecycleDeliveryCycle(store, ContextTransport(http), parent, clock, Settings()).RunOnceAsync());
        var after = (await store.ReadClosingReportDetailAsync("branch", "close", clock.Utc))!;
        Assert.Equal("Delivered", after.LifecycleDelivery!.DeliveryState);
        Assert.Equal(2, after.LifecycleDelivery.AttemptCount);
        Assert.Equal(before.Report, after.Report);
        Assert.Equal(JsonSerializer.Serialize(before.Lifecycle), JsonSerializer.Serialize(after.Lifecycle));
        Assert.Equal("Current", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", clock.Utc)).State);
        Assert.Single(parentBodies);
        Assert.Equal(2, contextBodies.Count);
        Assert.Equal(contextBodies[0], contextBodies[1]);
        Assert.Single(receiver.Data.Contexts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immutable_conflict_retains_pending_packet_and_does_not_reclose_or_overwrite(bool parentRepairConflict)
    {
        var store = await ClosedStore();
        await using var receiver = new Receiver();
        var clock = new Clock();
        var contextBodies = new List<string>();
        var parentBodies = new List<string>();
        string? conflictingKey = null;
        string? conflictingValue = null;
        using var handler = new Handler(async (request, token) =>
        {
            var context = request.RequestUri!.AbsolutePath == HttpClosingLifecycleTransport.Route;
            var body = await request.Content!.ReadAsStringAsync(token);
            (context ? contextBodies : parentBodies).Add(body);
            if (context && contextBodies.Count == 1)
            {
                using var parsed = JsonDocument.Parse(body);
                if (parentRepairConflict)
                {
                    receiver.Data.Parents.Clear();
                    var missing = await receiver.Call(true, body, token);
                    Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
                    conflictingKey = parsed.RootElement.GetProperty("reportId").GetString()!;
                    conflictingValue = parentBodies[0] + "\n";
                    receiver.Data.Parents.Add(conflictingKey, conflictingValue);
                    return missing;
                }
                conflictingKey = parsed.RootElement.GetProperty("contextId").GetString()!;
                conflictingValue = body + "\n";
                receiver.Data.Contexts.Add(conflictingKey, conflictingValue);
            }
            return await receiver.Call(context, body, token);
        });
        using var http = new HttpClient(handler);
        var parent = ParentTransport(http);
        Assert.True(await new ClosingReportDeliveryCycle(store, parent, clock, Settings()).RunOnceAsync());
        var before = (await store.ReadClosingReportDetailAsync("branch", "close", Now))!;
        Assert.True(await new ClosingLifecycleDeliveryCycle(store, ContextTransport(http), parent, clock, Settings()).RunOnceAsync());
        var after = (await store.ReadClosingReportDetailAsync("branch", "close", Now))!;
        Assert.Equal("RetryScheduled", after.LifecycleDelivery!.DeliveryState);
        Assert.Equal(parentRepairConflict ? "ParentUnavailable" : "CentralRejected", after.LifecycleDelivery.LastFailure);
        Assert.Equal(before.Report, after.Report);
        Assert.Equal(JsonSerializer.Serialize(before.Lifecycle), JsonSerializer.Serialize(after.Lifecycle));
        Assert.Equal("Pending", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now)).State);
        Assert.Single(contextBodies);
        Assert.Equal(parentRepairConflict ? 2 : 1, parentBodies.Count);
        if (parentRepairConflict) { Assert.Equal(parentBodies[0], parentBodies[1]); }
        Assert.Equal(conflictingValue, (parentRepairConflict ? receiver.Data.Parents : receiver.Data.Contexts)[conflictingKey!]);
        Assert.False(await new ClosingLifecycleDeliveryCycle(store, ContextTransport(http), parent, clock, Settings()).RunOnceAsync());
    }

    [Fact]
    public async Task Wrong_authenticated_branch_cannot_confirm_context_then_correct_identity_recovers_exact_packet()
    {
        var store = await ClosedStore();
        await using var receiver = new Receiver();
        var clock = new Clock(); var wrongIdentity = false;
        var contextBodies = new List<string>();
        using var handler = new Handler(async (request, token) =>
        {
            var context = request.RequestUri!.AbsolutePath == HttpClosingLifecycleTransport.Route;
            var body = await request.Content!.ReadAsStringAsync(token);
            if (context) { contextBodies.Add(body); }
            return await receiver.Call(context, body, token, context && wrongIdentity ? "other-branch" : "branch");
        });
        using var http = new HttpClient(handler);
        var parent = ParentTransport(http);
        Assert.True(await new ClosingReportDeliveryCycle(store, parent, clock, Settings()).RunOnceAsync());
        wrongIdentity = true;
        Assert.True(await new ClosingLifecycleDeliveryCycle(store, ContextTransport(http), parent, clock, Settings()).RunOnceAsync());
        Assert.Empty(receiver.Data.Contexts);
        Assert.Equal("CentralRejected", (await store.ReadClosingReportDetailAsync("branch", "close", Now))!.LifecycleDelivery!.LastFailure);
        Assert.Equal("Pending", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now)).State);
        clock.Utc = Now.AddSeconds(10); wrongIdentity = false;
        store = new SqliteTableSessionCommandStore(_path);
        Assert.True(await new ClosingLifecycleDeliveryCycle(store, ContextTransport(http), parent, clock, Settings()).RunOnceAsync());
        Assert.Equal("Current", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", clock.Utc)).State);
        Assert.Single(receiver.Data.Contexts); Assert.Single(receiver.Data.Parents);
        Assert.Equal(2, contextBodies.Count); Assert.Equal(contextBodies[0], contextBodies[1]);
    }

    private async Task<SqliteTableSessionCommandStore> ClosedStore()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        var command = new StoreLifecycleCommand("open", "store", "shift", StoreLifecycleAction.OpenStore, 500,
            new("employee", "device", Now, new(2026, 11, 22)));
        await store.ExecuteStoreLifecycleAsync("branch", command);
        await store.ExecuteStoreLifecycleAsync("branch", command with { RequestId = "close", Action = StoreLifecycleAction.CloseStore, CashAmount = 490 });
        return store;
    }
    private static IOptions<ClosingReportDeliveryOptions> Settings() => Options.Create(new ClosingReportDeliveryOptions
    { Enabled = true, LifecycleEnabled = true, BranchId = "branch", RetryDelay = TimeSpan.FromSeconds(10) });
    private static HttpClosingReportTransport ParentTransport(HttpClient http) => new(http,
        new("https://central.example/api/v1/closing-reports"), TimeSpan.FromSeconds(2), TimeProvider.System);
    private static HttpClosingLifecycleTransport ContextTransport(HttpClient http) => new(http,
        new("https://central.example/api/v1/closing-reports/lifecycle"), TimeSpan.FromSeconds(2), TimeProvider.System);

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
        internal async Task<HttpResponseMessage> Call(bool context, string body, CancellationToken token, string branch = "branch")
        {
            var name = context ? CentralClosingReportContextEndpoints.EndpointName : CentralClosingReportEndpoints.EndpointName;
            var route = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints).Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
            var request = new DefaultHttpContext
            {
                RequestServices = _app.Services,
                RequestAborted = token,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CentralCloudClaimTypes.BranchId, branch)], "synthetic-branch-certificate"))
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
