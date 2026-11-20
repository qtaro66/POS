using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class ClosingReportDeliveryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"closing-delivery-{Guid.NewGuid():N}.db");
    private static readonly ClosingReportLease Report = new("branch", "report", "request", "CloseStore",
        "{\"schemaVersion\":1,\"reportId\":\"report\",\"branchId\":\"branch\",\"note\":\"ภาษาไทย\"}",
        "lease", "worker", Now.AddMinutes(1), 1);
    private static string Acknowledgement(ClosingReportLease report, string status = "Accepted") =>
        JsonSerializer.Serialize(new { schemaVersion = 1, branchId = report.BranchId, reportId = report.ReportId, status });
    private static HttpClosingReportTransport Transport(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), new("https://central.example/api/v1/closing-reports"),
            timeout ?? TimeSpan.FromSeconds(2), TimeProvider.System);
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("Accepted")]
    [InlineData("AlreadyAccepted")]
    public async Task Exact_persisted_payload_is_posted_and_matching_acknowledgement_is_required(string status)
    {
        using var handler = new Handler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://central.example/api/v1/closing-reports", request.RequestUri!.AbsoluteUri);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal(Encoding.UTF8.GetBytes(Report.PayloadJson), await request.Content.ReadAsByteArrayAsync(token));
            return Json(Acknowledgement(Report, status));
        });
        Assert.Null(await Transport(handler).SendAsync(Report, default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":2,\"branchId\":\"branch\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":\"1\",\"branchId\":\"branch\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"foreign\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"reportId\":\"foreign\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"reportId\":\"report\",\"status\":\"Queued\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"reportId\":\"report\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"reportId\":null,\"status\":\"Accepted\"}")]
    public async Task Malformed_foreign_or_ambiguous_acknowledgements_remain_unconfirmed(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(body)));
        Assert.Equal(ClosingReportDeliveryFailure.AcknowledgmentUnavailable, await Transport(handler).SendAsync(Report, default));
    }

    [Theory]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task Non_200_responses_and_redirects_do_not_confirm_delivery(int status)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(Acknowledgement(Report), (HttpStatusCode)status)));
        Assert.Equal(ClosingReportDeliveryFailure.CentralRejected, await Transport(handler).SendAsync(Report, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_acknowledgement_is_bounded_even_without_content_length(bool chunked)
    {
        using var handler = new Handler((_, _) =>
        {
            var response = Json(new string(' ', 4097) + Acknowledgement(Report));
            if (chunked) { response.Content = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes(new string(' ', 4097) + Acknowledgement(Report)))) { Headers = { ContentLength = null } }; }
            return Task.FromResult(response);
        });
        Assert.Equal(ClosingReportDeliveryFailure.AcknowledgmentUnavailable, await Transport(handler).SendAsync(Report, default));
    }

    [Fact]
    public async Task Transport_failure_and_deadline_preserve_retryable_outcome_but_shutdown_cancellation_propagates()
    {
        using var lost = new Handler((_, _) => throw new HttpRequestException("response lost"));
        Assert.Equal(ClosingReportDeliveryFailure.TransportUnavailable, await Transport(lost).SendAsync(Report, default));
        using var stalled = new Handler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Json("{}"); });
        Assert.Equal(ClosingReportDeliveryFailure.TransportUnavailable,
            await Transport(stalled, TimeSpan.FromMilliseconds(20)).SendAsync(Report, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Transport(stalled).SendAsync(Report, cancelled.Token));
    }

    private async Task<SqliteTableSessionCommandStore> Closed()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        var open = new StoreLifecycleCommand("open", "store", "shift", StoreLifecycleAction.OpenStore, 500,
            new("employee", "device", Now, new(2026, 11, 20)));
        await store.ExecuteStoreLifecycleAsync("branch", open);
        await store.ExecuteStoreLifecycleAsync("branch", open with { RequestId = "close", Action = StoreLifecycleAction.CloseStore, CashAmount = 490 });
        return store;
    }
    private static ClosingReportDeliveryCycle Cycle(SqliteTableSessionCommandStore store, IClosingReportTransport transport, TimeProvider time) =>
        new(store, transport, time, Options.Create(new ClosingReportDeliveryOptions
        {
            Enabled = true, BranchId = "branch", RetryDelay = TimeSpan.FromSeconds(10), MaximumRetryDelay = TimeSpan.FromSeconds(20)
        }));

    [Fact]
    public async Task Lost_acknowledgement_restarts_with_same_payload_then_accepts_duplicate_and_marks_delivered()
    {
        var store = await Closed();
        var clock = new Clock();
        var posted = new List<string>();
        var calls = 0;
        using var handler = new Handler(async (request, token) =>
        {
            posted.Add(await request.Content!.ReadAsStringAsync(token));
            using var payload = JsonDocument.Parse(posted[^1]);
            if (++calls == 1) { throw new HttpRequestException("central persisted, response lost"); }
            return Json(JsonSerializer.Serialize(new
            {
                schemaVersion = 1, branchId = "branch", reportId = payload.RootElement.GetProperty("reportId").GetString(), status = "AlreadyAccepted"
            }));
        });
        Assert.True(await Cycle(store, Transport(handler), clock).RunOnceAsync());
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", clock.Utc)).PendingCount);
        var reopened = new SqliteTableSessionCommandStore(_path);
        Assert.False(await Cycle(reopened, Transport(handler), clock).RunOnceAsync());
        clock.Utc = Now.AddSeconds(10);
        Assert.True(await Cycle(reopened, Transport(handler), clock).RunOnceAsync());
        Assert.Equal(2, posted.Count);
        Assert.Equal(posted[0], posted[1]);
        var status = await reopened.ReadClosingReportOutboxAsync("branch", clock.Utc);
        Assert.Equal(0, status.PendingCount);
        Assert.Equal(1, status.DeliveredCount);
    }

    [Fact]
    public async Task Expired_sender_cannot_confirm_after_another_worker_reclaims_report()
    {
        var store = await Closed();
        var clock = new Clock();
        ClosingReportLease? reclaimed = null;
        var transport = new DelegateTransport(async (_, token) =>
        {
            clock.Utc = Now.AddMinutes(1);
            reclaimed = await new SqliteTableSessionCommandStore(_path).ClaimClosingReportAsync("branch", "replacement", clock.Utc, TimeSpan.FromMinutes(1), token);
            return null;
        });
        Assert.True(await Cycle(store, transport, clock).RunOnceAsync());
        Assert.NotNull(reclaimed);
        var status = await store.ReadClosingReportOutboxAsync("branch", clock.Utc);
        Assert.Equal(1, status.PendingCount);
        Assert.Equal(0, status.DeliveredCount);
        Assert.Equal(1, status.LeasedCount);
    }

    [Fact]
    public async Task Cancellation_leaves_durable_lease_for_restart_recovery()
    {
        var store = await Closed();
        var clock = new Clock();
        using var cancellation = new CancellationTokenSource();
        var transport = new DelegateTransport((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult<ClosingReportDeliveryFailure?>(null);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cycle(store, transport, clock).RunOnceAsync(cancellation.Token));
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", Now)).LeasedCount);
        Assert.Null(await store.ClaimClosingReportAsync("branch", "restart", Now.AddSeconds(59), TimeSpan.FromMinutes(1)));
        Assert.NotNull(await store.ClaimClosingReportAsync("branch", "restart", Now.AddMinutes(1), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Disabled_default_registers_no_delivery_worker_transport_or_database_writer()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddClosingReportDelivery(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>().Value.Enabled);
        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.Null(provider.GetService<IClosingReportTransport>());
        Assert.Null(provider.GetService<ClosingReportDeliveryCycle>());
        Assert.Null(provider.GetService<SqliteTableSessionCommandStore>());
    }

    [Theory]
    [InlineData("ClosingReportDelivery:CentralOrigin", "http://central.example")]
    [InlineData("ClosingReportDelivery:CentralOrigin", "https://central.example/private")]
    [InlineData("ClosingReportDelivery:CentralOrigin", "https://user:password@central.example")]
    [InlineData("ClosingReportDelivery:BranchId", " branch")]
    [InlineData("ClosingReportDelivery:RequestTimeout", "00:00:00")]
    [InlineData("ClosingReportDelivery:LeaseDuration", "00:00:10")]
    [InlineData("ClosingReportDelivery:IdleDelay", "00:00:00")]
    [InlineData("ClosingReportDelivery:RetryDelay", "00:00:00")]
    [InlineData("ClosingReportDelivery:MaximumRetryDelay", "00:00:01")]
    [InlineData("BranchCentralMtls:Enabled", "false")]
    [InlineData("BranchOrdering:Enabled", "false")]
    [InlineData("BranchOrdering:DatabasePath", "relative.db")]
    public void Unsafe_delivery_configuration_is_rejected(string key, string value)
    {
        var values = ValidConfiguration();
        values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IBranchCloudDeliveryAuthenticatedHttpHandlerFactory>(new HandlerFactory());
        services.AddClosingReportDelivery(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>().Value);
    }

    [Fact]
    public void Enabled_delivery_requires_authenticated_handler_even_when_mtls_setting_is_true()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ValidConfiguration()).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddClosingReportDelivery(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>().Value);
    }

    [Fact]
    public async Task Retry_delay_grows_to_cap_without_sending_early()
    {
        var store = await Closed();
        var clock = new Clock();
        var attempts = new List<int>();
        var failure = new DelegateTransport((lease, _) =>
        {
            attempts.Add(lease.AttemptCount);
            return Task.FromResult<ClosingReportDeliveryFailure?>(ClosingReportDeliveryFailure.CentralRejected);
        });
        var cycle = Cycle(store, failure, clock);
        Assert.True(await cycle.RunOnceAsync());
        clock.Utc = Now.AddSeconds(9);
        Assert.False(await cycle.RunOnceAsync());
        clock.Utc = Now.AddSeconds(10);
        Assert.True(await cycle.RunOnceAsync());
        clock.Utc = Now.AddSeconds(29);
        Assert.False(await cycle.RunOnceAsync());
        clock.Utc = Now.AddSeconds(30);
        Assert.True(await cycle.RunOnceAsync());
        clock.Utc = Now.AddSeconds(49);
        Assert.False(await cycle.RunOnceAsync());
        clock.Utc = Now.AddSeconds(50);
        Assert.True(await cycle.RunOnceAsync());
        Assert.Equal(new[] { 1, 2, 3, 4 }, attempts);
        Assert.Equal(0, (await store.ReadClosingReportOutboxAsync("branch", clock.Utc)).DeliveredCount);
    }

    [Fact]
    public void Valid_enabled_composition_registers_one_worker_and_uses_authenticated_handler()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ValidConfiguration()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        var factory = new HandlerFactory();
        services.AddSingleton<IBranchCloudDeliveryAuthenticatedHttpHandlerFactory>(factory);
        services.AddClosingReportDelivery(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>().Value.Enabled);
        Assert.IsType<HttpClosingReportTransport>(provider.GetRequiredService<IClosingReportTransport>());
        Assert.Single(provider.GetServices<IHostedService>());
        Assert.NotNull(provider.GetRequiredService<ClosingReportDeliveryCycle>());
        Assert.Equal(1, factory.Calls);
    }
    private Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["ClosingReportDelivery:Enabled"] = "true", ["ClosingReportDelivery:BranchId"] = "branch",
        ["ClosingReportDelivery:CentralOrigin"] = "https://central.example",
        ["BranchOrdering:Enabled"] = "true", ["BranchOrdering:DatabasePath"] = _path,
        ["BranchCentralMtls:Enabled"] = "true"
    };
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Utc { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class DelegateTransport(Func<ClosingReportLease, CancellationToken, Task<ClosingReportDeliveryFailure?>> send) : IClosingReportTransport
    {
        public Task<ClosingReportDeliveryFailure?> SendAsync(ClosingReportLease report, CancellationToken token) => send(report, token);
    }
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private sealed class HandlerFactory : IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
    {
        internal int Calls { get; private set; }
        public HttpMessageHandler CreateHandler() { Calls++; return new Handler((_, _) => Task.FromResult(Json("{}"))); }
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
