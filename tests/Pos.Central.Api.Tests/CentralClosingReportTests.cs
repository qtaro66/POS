using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class CentralClosingReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 21);
    private static string Payload(string branch = "branch", string request = "close", string store = "store", string shift = "shift") =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            reportId = "closing:" + Convert.ToHexString(Encoding.UTF8.GetBytes(branch)) + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(request)),
            branchId = branch,
            requestId = request,
            storeSessionId = store,
            shiftId = shift,
            operation = "CloseStore",
            businessDate = "2026-11-21",
            closing = new
            {
                StoreSessionId = store,
                ShiftId = shift,
                ClosesStore = true,
                ExpectedCashAmount = 500m,
                ActualCashAmount = 490m,
                CashDifference = -10m,
                ReconciliationStatus = "Reconciled",
                Context = new { EmployeeId = "employee", DeviceId = "device", OccurredAt = Now, BranchLocalDate = Day }
            },
            cashSummary = new { Source = "CashSales", Store = new { ReceiptCount = 0, AppliedCashAmount = 0m } }
        });

    private static ClaimsPrincipal Principal(params string[] branches) => new(new ClaimsIdentity(
        branches.Select(branch => new Claim(CentralCloudClaimTypes.BranchId, branch)), "test-certificate"));

    [Fact]
    public async Task Authorized_route_accepts_original_payload_and_acknowledges_exact_duplicate_without_rewriting()
    {
        await using var fixture = new Fixture();
        var payload = Payload();
        var accepted = await fixture.Call(payload);
        Assert.Equal(200, accepted.Status);
        Assert.Equal("no-store", accepted.CacheControl);
        using var document = JsonDocument.Parse(accepted.Body);
        Assert.Equal("Accepted", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("branch", document.RootElement.GetProperty("branchId").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(4, document.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("employee", accepted.Body);
        Assert.Equal(payload, fixture.Store.Reports.Single().PayloadJson);
        var duplicate = await fixture.Call(payload);
        Assert.Equal(200, duplicate.Status);
        Assert.Contains("AlreadyAccepted", duplicate.Body);
        Assert.Single(fixture.Store.Reports);
        var changed = await fixture.Call(payload.Replace("\"cashSummary\":", "\"extra\":true,\"cashSummary\":"));
        Assert.Equal(409, changed.Status);
        Assert.Equal(payload, fixture.Store.Reports.Single().PayloadJson);
        Assert.Equal(409, (await fixture.Call(Payload(request: "different-request"))).Status);
    }

    [Fact]
    public async Task Concurrent_duplicates_produce_one_saved_report_and_changed_content_remains_a_conflict()
    {
        await using var fixture = new Fixture();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.Call(Payload())));
        Assert.All(responses, response => Assert.Equal(200, response.Status));
        Assert.Single(responses, response => response.Body.Contains("\"Accepted\"", StringComparison.Ordinal));
        Assert.Single(fixture.Store.Reports);
    }

    [Theory]
    [InlineData("none", 401)]
    [InlineData("duplicate", 401)]
    [InlineData("foreign", 403)]
    public async Task Missing_ambiguous_or_foreign_identity_never_reaches_readiness_or_storage(string kind, int expected)
    {
        await using var fixture = new Fixture();
        var identity = kind switch
        {
            "duplicate" => Principal("branch", "branch"),
            "foreign" => Principal("other"),
            _ => new ClaimsPrincipal()
        };
        Assert.Equal(expected, (await fixture.Call(Payload(), identity)).Status);
        Assert.Equal(0, fixture.Store.Calls);
        Assert.Equal(0, fixture.Readiness.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("version")]
    [InlineData("duplicate-root")]
    [InlineData("duplicate-nested")]
    [InlineData("wrong-money")]
    [InlineData("wrong-closing")]
    [InlineData("wrong-context")]
    public async Task Invalid_envelopes_are_rejected_without_echoing_input_or_writing(string mutation)
    {
        var payload = mutation switch
        {
            "version" => Payload().Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            "duplicate-root" => Payload().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            "duplicate-nested" => Payload().Replace("\"ExpectedCashAmount\":500", "\"ExpectedCashAmount\":500,\"ExpectedCashAmount\":500"),
            "wrong-money" => Payload().Replace("\"CashDifference\":-10", "\"CashDifference\":-11"),
            "wrong-closing" => Payload().Replace("\"ClosesStore\":true", "\"ClosesStore\":false"),
            "wrong-context" => Payload().Replace("\"EmployeeId\":\"employee\"", "\"EmployeeId\":null"),
            _ => mutation
        };
        await using var fixture = new Fixture();
        var response = await fixture.Call(payload);
        Assert.Equal(400, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        Assert.DoesNotContain("employee", response.Body);
        Assert.Equal(0, fixture.Store.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Body_limit_is_enforced_with_and_without_a_content_length(bool chunked)
    {
        await using var fixture = new Fixture();
        Assert.Equal(413, (await fixture.Call(new string(' ', CentralClosingReportEndpoints.MaximumBodyBytes + 1), chunked: chunked)).Status);
        Assert.Equal(0, fixture.Store.Calls);
    }

    [Fact]
    public async Task Invalid_utf8_and_content_type_are_rejected()
    {
        await using var fixture = new Fixture();
        Assert.Equal(400, (await fixture.CallBytes([0xff, 0xfe])).Status);
        Assert.Equal(415, (await fixture.Call(Payload(), contentType: "text/plain")).Status);
        Assert.Equal(0, fixture.Store.Calls);
    }

    [Fact]
    public async Task Unready_or_failed_storage_cannot_return_an_acknowledgement()
    {
        await using var fixture = new Fixture();
        fixture.Readiness.Ready = false;
        Assert.Equal(503, (await fixture.Call(Payload())).Status);
        Assert.Equal(0, fixture.Store.Calls);
        fixture.Readiness.Ready = true;
        fixture.Store.Failure = new ClosingReportStoreUnavailableException(new IOException("synthetic unavailable"));
        var failed = await fixture.Call(Payload());
        Assert.Equal(503, failed.Status);
        Assert.DoesNotContain("Accepted", failed.Body);
        Assert.DoesNotContain("synthetic unavailable", failed.Body);
        Assert.Empty(fixture.Store.Reports);
    }

    [Fact]
    public async Task Cancellation_propagates_without_acknowledging_storage()
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Call(Payload(), token: cancellation.Token));
        Assert.Empty(fixture.Store.Reports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_branch_outbox_sender_and_central_route_recover_lost_acknowledgement_without_duplicate_storage(bool closeStore)
    {
        var path = Path.Combine(Path.GetTempPath(), $"closing-wire-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTableSessionCommandStore(path);
            await store.InitializeAsync();
            var open = new StoreLifecycleCommand("open", "store", "shift", StoreLifecycleAction.OpenStore, 500,
                new("employee", "device", Now, Day));
            await store.ExecuteStoreLifecycleAsync("branch", open);
            await store.ExecuteStoreLifecycleAsync("branch", open with { RequestId = "close", Action = closeStore ? StoreLifecycleAction.CloseStore : StoreLifecycleAction.CloseShift, CashAmount = closeStore ? 490 : null });
            await using var receiver = new Fixture();
            var posted = new List<string>();
            var clock = new Clock();
            using var handler = new Handler(async (request, token) =>
            {
                var json = await request.Content!.ReadAsStringAsync(token);
                posted.Add(json);
                var response = await receiver.Call(json, token: token);
                Assert.Equal(200, response.Status);
                if (posted.Count == 1) { throw new HttpRequestException("response lost after durable test-store commit"); }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
            });
            using var http = new HttpClient(handler);
            var transport = new HttpClosingReportTransport(http, new("https://central.example/api/v1/closing-reports"), TimeSpan.FromSeconds(5), TimeProvider.System);
            var settings = Options.Create(new ClosingReportDeliveryOptions { Enabled = true, BranchId = "branch", RetryDelay = TimeSpan.FromSeconds(10) });
            Assert.True(await new ClosingReportDeliveryCycle(store, transport, clock, settings).RunOnceAsync());
            Assert.Single(receiver.Store.Reports);
            Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", clock.Utc)).PendingCount);
            clock.Utc = Now.AddSeconds(10);
            var reopened = new SqliteTableSessionCommandStore(path);
            Assert.True(await new ClosingReportDeliveryCycle(reopened, transport, clock, settings).RunOnceAsync());
            Assert.Equal(2, posted.Count);
            Assert.Equal(posted[0], posted[1]);
            Assert.Equal(posted[0], receiver.Store.Reports.Single().PayloadJson);
            Assert.Single(receiver.Store.Reports);
            var status = await reopened.ReadClosingReportOutboxAsync("branch", clock.Utc);
            Assert.Equal(0, status.PendingCount);
            Assert.Equal(1, status.DeliveredCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(path + suffix); }
        }
    }

    [Fact]
    public async Task Route_has_shared_branch_authorization_bounded_body_and_no_anonymous_access()
    {
        await using var fixture = new Fixture();
        Assert.Equal(HttpClosingReportTransport.Route, fixture.Route.RoutePattern.RawText);
        Assert.Equal(CentralClosingReportEndpoints.EndpointName, fixture.Route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Equal(CentralClosingReportEndpoints.MaximumBodyBytes, fixture.Route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.Equal(CentralCloudIngestionAuthorizationDefaults.PolicyName,
            Assert.Single(fixture.Route.Metadata.GetOrderedMetadata<IAuthorizeData>()).Policy);
        Assert.Null(fixture.Route.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Disabled_composition_and_mapping_add_no_persistence_or_route()
    {
        var configuration = Config(false);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCentralClosingReports(configuration);
        await using var app = builder.Build();
        app.MapCentralClosingReportsIfEnabled(configuration);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources);
        Assert.Null(app.Services.GetService<IClosingReportIngestionStore>());
        Assert.Null(app.Services.GetService<IPostgresClosingReportReadinessProbe>());
        Assert.False(app.Services.GetRequiredService<IOptions<CentralClosingReportOptions>>().Value.Enabled);
    }

    [Theory]
    [InlineData("CentralCloudIngestion:Enabled", "false")]
    [InlineData("CentralBranchMtls:Enabled", "false")]
    [InlineData("CentralCloudIngestion:AuthenticationScheme", "Other")]
    public void Enabling_without_shared_mtls_and_ingestion_is_rejected(string key, string value)
    {
        var configuration = Config(true, key, value);
        Assert.True(new CentralClosingReportOptionsValidator(configuration).Validate(null, new() { Enabled = true }).Failed);
    }

    [Fact]
    public async Task Enabled_composition_registers_durable_store_readiness_and_valid_options_without_opening_database()
    {
        var configuration = Config(true);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCentralClosingReports(configuration);
        await using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<CentralClosingReportOptions>>().Value.Enabled);
        Assert.NotNull(provider.GetRequiredService<IClosingReportIngestionStore>());
        Assert.NotNull(provider.GetRequiredService<IPostgresClosingReportReadinessProbe>());
    }

    [Fact]
    public async Task Health_check_matches_report_readiness_without_database_details()
    {
        var readiness = new Readiness { Ready = false };
        var check = new CentralClosingReportHealthCheck(readiness);
        var failed = await check.CheckHealthAsync(new());
        Assert.Equal(HealthStatus.Unhealthy, failed.Status);
        Assert.Null(failed.Exception);
        readiness.Ready = true;
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new())).Status);
    }

    [Fact]
    public async Task Https_guard_rejects_before_redirect_and_leaves_unrelated_routes_untouched()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        builder.UseCentralClosingReportHttpsOnly(Config(true));
        builder.Run(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        var app = builder.Build();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Path = CentralClosingReportEndpoints.Route;
        await app(context);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Path = "/health";
        await app(context);
        Assert.Equal(204, context.Response.StatusCode);
    }

    [Fact]
    public async Task Https_middleware_blocks_http_before_receiver_and_allows_https_through()
    {
        await using var fixture = new Fixture();
        using var services = new ServiceCollection().BuildServiceProvider();
        var pipeline = new ApplicationBuilder(services);
        pipeline.UseCentralClosingReportHttpsOnly(Config(true));
        pipeline.Run(fixture.Route.RequestDelegate!);
        var app = pipeline.Build();
        foreach (var scheme in new[] { "http", "https" })
        {
            var context = new DefaultHttpContext
            {
                RequestServices = fixture.Services,
                User = Principal("branch")
            };
            context.Request.Method = "POST";
            context.Request.Path = CentralClosingReportEndpoints.Route;
            context.Request.Scheme = scheme;
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Payload()));
            context.Response.Body = new MemoryStream();
            await app(context);
            Assert.Equal(scheme == "http" ? 400 : 200, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl);
            Assert.Equal(scheme == "http" ? 0 : 1, fixture.Readiness.Calls);
            Assert.Equal(scheme == "http" ? 0 : 1, fixture.Store.Calls);
        }
    }

    private static IConfiguration Config(bool enabled, string? key = null, string? value = null)
    {
        var entries = new Dictionary<string, string?>
        {
            ["CentralClosingReports:Enabled"] = enabled.ToString(),
            ["CentralCloudIngestion:Enabled"] = "true",
            ["CentralBranchMtls:Enabled"] = "true",
            ["CentralCloudIngestion:AuthenticationScheme"] = CentralBranchCertificateAuthentication.SchemeName,
            ["ConnectionStrings:CentralDatabase"] = "Host=127.0.0.1;Database=synthetic_closing"
        };
        if (key is not null) { entries[key] = value; }
        return new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        internal Store Store { get; } = new();
        internal Readiness Readiness { get; } = new();
        internal RouteEndpoint Route { get; }
        internal IServiceProvider Services => _app.Services;
        internal Fixture()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<IClosingReportIngestionStore>(Store);
            builder.Services.AddSingleton<IPostgresClosingReportReadinessProbe>(Readiness);
            _app = builder.Build();
            _app.MapCentralClosingReportsIfEnabled(Config(true));
            Route = Assert.IsType<RouteEndpoint>(Assert.Single(((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints)));
        }
        internal Task<Response> Call(string payload, ClaimsPrincipal? principal = null, bool chunked = false,
            string contentType = "application/json", string scheme = "https", CancellationToken token = default) =>
            CallBytes(Encoding.UTF8.GetBytes(payload), principal, chunked, contentType, scheme, token);
        internal async Task<Response> CallBytes(byte[] body, ClaimsPrincipal? principal = null, bool chunked = false,
            string contentType = "application/json", string scheme = "https", CancellationToken token = default)
        {
            var context = new DefaultHttpContext { RequestServices = _app.Services, User = principal ?? Principal("branch"), RequestAborted = token };
            context.Request.Method = "POST";
            context.Request.Path = CentralClosingReportEndpoints.Route;
            context.Request.Scheme = scheme;
            context.Request.ContentType = contentType;
            if (!chunked) { context.Request.ContentLength = body.Length; }
            context.Request.Body = new MemoryStream(body);
            context.Response.Body = new MemoryStream();
            await Route.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            return new(context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(), context.Response.Headers.CacheControl.ToString());
        }
        public ValueTask DisposeAsync() => _app.DisposeAsync();
    }
    private sealed record Response(int Status, string Body, string CacheControl);
    private sealed class Readiness : IPostgresClosingReportReadinessProbe
    {
        internal bool Ready { get; set; } = true;
        internal int Calls { get; private set; }
        public Task<bool> IsReadyAsync(CancellationToken token = default) { Calls++; token.ThrowIfCancellationRequested(); return Task.FromResult(Ready); }
    }
    // Simulated durable receiver shared across HTTP attempts; PostgreSQL persistence is tested separately.
    private sealed class Store : IClosingReportIngestionStore
    {
        private readonly object _gate = new();
        internal List<ClosingReportEnvelope> Reports { get; } = [];
        internal int Calls { get; private set; }
        internal Exception? Failure { get; set; }
        public Task<ClosingReportPersistenceResult> PersistAsync(ClosingReportEnvelope report, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                Calls++;
                if (Failure is not null) { throw Failure; }
                var matches = Reports.Where(saved => saved.ReportId == report.ReportId ||
                    (saved.BranchId == report.BranchId && (saved.RequestId == report.RequestId ||
                    (saved.StoreSessionId == report.StoreSessionId && saved.ShiftId == report.ShiftId)))).ToArray();
                if (matches.Length > 0)
                {
                    return Task.FromResult(matches.Length == 1 && matches[0].PayloadJson == report.PayloadJson
                        ? ClosingReportPersistenceResult.AlreadyAccepted : ClosingReportPersistenceResult.Conflict);
                }
                Reports.Add(report);
                return Task.FromResult(ClosingReportPersistenceResult.Accepted);
            }
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Utc { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
}
