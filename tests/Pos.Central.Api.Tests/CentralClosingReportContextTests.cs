using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;
using Pos.Domain.StoreSessions;

namespace Pos.Central.Api.Tests;

public sealed class CentralClosingReportContextTests
{
    private static string Payload()
    {
        var day = new DateOnly(2026, 11, 22);
        var opened = new StoreOperationContext("employee", "device", new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero), day);
        var closing = new ShiftClosing("store", "shift", true, 500, 490, -10, opened);
        const string report = "closing:6272616E6368:72657175657374";
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            contextId = "lifecycle:" + report,
            reportId = report,
            branchId = "branch",
            requestId = "request",
            context = new
            {
                SchemaVersion = 1,
                BranchId = "branch",
                RequestId = "request",
                StoreSessionId = "store",
                ClosingShiftId = "shift",
                BusinessDate = day,
                OpeningCashAmount = 500m,
                Opened = opened,
                ClosesStore = true,
                Shifts = new[] { new { ShiftId = "shift", OpeningCashAmount = 500m, Opened = opened, Closing = closing } }
            }
        });
    }
    [Theory]
    [InlineData(ClosingReportContextPersistenceResult.Accepted, 200, "Accepted")]
    [InlineData(ClosingReportContextPersistenceResult.AlreadyAccepted, 200, "AlreadyAccepted")]
    [InlineData(ClosingReportContextPersistenceResult.ParentMissing, 409, "ClosingReportParentMissing")]
    [InlineData(ClosingReportContextPersistenceResult.Conflict, 409, "ClosingReportContextConflict")]
    public async Task Result_mapping_preserves_identity_without_echoing_context(ClosingReportContextPersistenceResult result, int status, string marker)
    {
        await using var f = new Fixture(); f.Store.Result = result;
        var response = await f.Call(Encoding.UTF8.GetBytes(Payload()));
        Assert.Equal(status, response.Status); Assert.Contains(marker, response.Body); Assert.DoesNotContain("employee", response.Body);
        if (status == 200) { using var doc = JsonDocument.Parse(response.Body); Assert.Equal(5, doc.RootElement.EnumerateObject().Count()); Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32()); }
    }
    [Theory]
    [InlineData(null, 401)]
    [InlineData("other", 403)]
    public async Task Missing_or_foreign_identity_cannot_reach_storage(string? branch, int status)
    {
        await using var f = new Fixture(); Assert.Equal(status, (await f.Call(Encoding.UTF8.GetBytes(Payload()), branch: branch)).Status);
        Assert.Equal(0, f.Store.Calls); Assert.Equal(0, f.Probe.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_framing_modes_enforce_one_megabyte(bool chunked)
    {
        await using var f = new Fixture(); Assert.Equal(413, (await f.Call(new byte[CentralClosingReportContextEndpoints.MaximumBodyBytes + 1], chunked)).Status); Assert.Equal(0, f.Store.Calls);
    }
    [Fact]
    public async Task Context_larger_than_base_report_limit_is_accepted()
    {
        await using var f = new Fixture(); var body = Payload().Insert(1, "\"padding\":\"" + new string('x', 70000) + "\",");
        Assert.Equal(200, (await f.Call(Encoding.UTF8.GetBytes(body))).Status);
    }
    [Fact]
    public async Task Malformed_utf8_nested_duplicates_and_wrong_content_type_do_not_write()
    {
        await using var f = new Fixture();
        Assert.Equal(400, (await f.Call([0xff, 0xfe])).Status);
        Assert.Equal(400, (await f.Call(Encoding.UTF8.GetBytes(Payload().Replace("\"EmployeeId\":", "\"EmployeeId\":\"duplicate\",\"EmployeeId\":")))).Status);
        Assert.Equal(415, (await f.Call(Encoding.UTF8.GetBytes(Payload()), contentType: "text/plain")).Status); Assert.Equal(0, f.Store.Calls);
    }
    [Fact]
    public async Task Unready_and_failed_store_are_sanitized_without_acknowledgement()
    {
        await using var f = new Fixture(); f.Probe.Ready = false;
        Assert.Equal(503, (await f.Call(Encoding.UTF8.GetBytes(Payload()))).Status); Assert.Equal(0, f.Store.Calls);
        f.Probe.Ready = true; f.Store.Failure = true;
        var response = await f.Call(Encoding.UTF8.GetBytes(Payload())); Assert.Equal(503, response.Status); Assert.DoesNotContain("private", response.Body); Assert.DoesNotContain("Accepted", response.Body);
    }
    [Fact]
    public async Task Disabled_feature_does_not_map_context_route()
    {
        var builder = WebApplication.CreateBuilder(); await using var app = builder.Build();
        app.MapCentralClosingReportContextsIfEnabled(new ConfigurationBuilder().Build());
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints));
    }
    [Fact]
    public async Task Https_middleware_covers_lifecycle_child_route_before_storage()
    {
        await using var f = new Fixture();
        using var services = new ServiceCollection().BuildServiceProvider();
        var pipeline = new ApplicationBuilder(services);
        pipeline.UseCentralClosingReportHttpsOnly(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CentralClosingReports:Enabled"] = "true" }).Build());
        pipeline.Run(_ => throw new InvalidOperationException("HTTP must not reach endpoint"));
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = CentralClosingReportContextEndpoints.Route; context.Request.Scheme = "http";
        context.Response.Body = new MemoryStream(); await pipeline.Build()(context);
        Assert.Equal(400, context.Response.StatusCode); Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dedicated_context_health_reflects_readiness(bool ready)
    {
        var probe = new Probe { Ready = ready };
        var result = await new CentralClosingReportContextHealthCheck(probe).CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
        Assert.Equal(ready ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy : Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication app;
        internal Store Store { get; } = new(); internal Probe Probe { get; } = new(); private readonly RouteEndpoint route;
        internal Fixture()
        {
            var builder = WebApplication.CreateBuilder(); builder.Services.AddSingleton<IClosingReportContextIngestionStore>(Store); builder.Services.AddSingleton<IPostgresClosingReportContextReadinessProbe>(Probe);
            app = builder.Build(); app.MapCentralClosingReportContextsIfEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CentralClosingReports:Enabled"] = "true" }).Build());
            route = Assert.IsType<RouteEndpoint>(Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints)));
        }
        internal async Task<(int Status, string Body)> Call(byte[] bytes, bool chunked = false, string? branch = "branch", string contentType = "application/json")
        {
            var identity = branch is null ? new ClaimsPrincipal() : new ClaimsPrincipal(new ClaimsIdentity([new Claim(CentralCloudClaimTypes.BranchId, branch)], "certificate"));
            var context = new DefaultHttpContext { RequestServices = app.Services, User = identity }; context.Request.Method = "POST"; context.Request.Path = CentralClosingReportContextEndpoints.Route; context.Request.Scheme = "https";
            context.Request.ContentType = contentType; if (!chunked) { context.Request.ContentLength = bytes.Length; }
            context.Request.Body = new MemoryStream(bytes); context.Response.Body = new MemoryStream();
            await route.RequestDelegate!(context); Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString()); context.Response.Body.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }
        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
    private sealed class Probe : IPostgresClosingReportContextReadinessProbe
    {
        internal bool Ready = true; internal int Calls;
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(Ready); }
    }
    private sealed class Store : IClosingReportContextIngestionStore
    {
        internal ClosingReportContextPersistenceResult Result; internal int Calls; internal bool Failure;
        public Task<ClosingReportContextPersistenceResult> PersistAsync(ClosingReportContextEnvelope report, CancellationToken cancellationToken = default)
        { Calls++; if (Failure) { throw new ClosingReportStoreUnavailableException(new IOException("private")); } return Task.FromResult(Result); }
    }
}
