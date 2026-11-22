using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class ClosingLifecycleDeliveryTests : IDisposable
{
    [Fact]
    public void Lifecycle_delivery_defaults_off_and_requires_base_delivery()
    {
        Assert.False(new ClosingReportDeliveryOptions().LifecycleEnabled);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var serviceProbe = (Microsoft.Extensions.DependencyInjection.IServiceProviderIsService)provider.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceProviderIsService))!;
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var validator = new ClosingReportDeliveryOptionsValidator(configuration, serviceProbe);
        Assert.True(validator.Validate(null, new ClosingReportDeliveryOptions()).Succeeded);
        Assert.True(validator.Validate(null, new ClosingReportDeliveryOptions { LifecycleEnabled = true }).Failed);
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lifecycle-delivery-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly ClosingLifecycleLease Message = new("branch", "lifecycle:report", "report", "request", "CloseStore",
        "{\"contextId\":\"lifecycle:report\",\"note\":\"ภาษาไทย\"}", "{\"reportId\":\"report\"}", "lease", "worker", Now.AddMinutes(1), 1);
    private static string Ack(ClosingLifecycleLease lease, string status = "Accepted") => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        branchId = lease.BranchId,
        contextId = lease.ContextId,
        reportId = lease.ReportId,
        status
    });
    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpClosingLifecycleTransport Transport(HttpMessageHandler handler, TimeSpan? deadline = null) =>
        new(new HttpClient(handler), new("https://central.example/api/v1/closing-reports/lifecycle"), deadline ?? TimeSpan.FromSeconds(2), TimeProvider.System);

    [Theory]
    [InlineData("Accepted")]
    [InlineData("AlreadyAccepted")]
    public async Task Context_transport_preserves_exact_body_and_requires_context_specific_ack(string status)
    {
        using var handler = new Handler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(HttpClosingLifecycleTransport.Route, request.RequestUri!.AbsolutePath);
            Assert.Equal(Encoding.UTF8.GetBytes(Message.PayloadJson), await request.Content!.ReadAsByteArrayAsync(token));
            return Response(Ack(Message, status));
        });
        Assert.Null(await Transport(handler).SendAsync(Message, default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"other\",\"contextId\":\"lifecycle:report\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"contextId\":\"wrong\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"contextId\":\"lifecycle:report\",\"reportId\":\"wrong\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":\"1\",\"branchId\":\"branch\",\"contextId\":\"lifecycle:report\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"branch\",\"contextId\":\"lifecycle:report\",\"contextId\":\"lifecycle:report\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"branchId\":\"\\uD800\",\"contextId\":\"lifecycle:report\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    [InlineData("{\"schemaVersion\":1,\"\\uD800\":true,\"branchId\":\"branch\",\"contextId\":\"lifecycle:report\",\"reportId\":\"report\",\"status\":\"Accepted\"}")]
    public async Task Ambiguous_or_parent_only_ack_cannot_confirm_context(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(body)));
        Assert.Equal(ClosingLifecycleFailure.AcknowledgmentUnavailable, await Transport(handler).SendAsync(Message, default));
    }

    [Fact]
    public async Task Parent_repair_is_requested_only_for_the_specific_conflict_code()
    {
        using var parent = new Handler((_, _) => Task.FromResult(Response("{\"error\":\"ClosingReportParentMissing\"}", HttpStatusCode.Conflict)));
        Assert.Equal(ClosingLifecycleFailure.ParentUnavailable, await Transport(parent).SendAsync(Message, default));
        using var conflict = new Handler((_, _) => Task.FromResult(Response("{\"error\":\"ClosingReportContextConflict\"}", HttpStatusCode.Conflict)));
        Assert.Equal(ClosingLifecycleFailure.CentralRejected, await Transport(conflict).SendAsync(Message, default));
        using var redirect = new Handler((_, _) => Task.FromResult(Response(Ack(Message), HttpStatusCode.Redirect)));
        Assert.Equal(ClosingLifecycleFailure.CentralRejected, await Transport(redirect).SendAsync(Message, default));
    }

    [Fact]
    public async Task Response_and_deadline_are_bounded_and_shutdown_propagates()
    {
        using var oversized = new Handler((_, _) => Task.FromResult(Response(new string(' ', 8193))));
        Assert.Equal(ClosingLifecycleFailure.AcknowledgmentUnavailable, await Transport(oversized).SendAsync(Message, default));
        using var stalled = new Handler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Response("{}"); });
        Assert.Equal(ClosingLifecycleFailure.TransportUnavailable, await Transport(stalled, TimeSpan.FromMilliseconds(20)).SendAsync(Message, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Transport(stalled).SendAsync(Message, cancelled.Token));
    }

    private async Task<(SqliteTableSessionCommandStore Store, ClosingReportLease Parent)> Closed()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        var command = new StoreLifecycleCommand("open", "store", "shift", StoreLifecycleAction.OpenStore, 500,
            new("employee", "device", Now, new(2026, 11, 22)));
        await store.ExecuteStoreLifecycleAsync("branch", command);
        await store.ExecuteStoreLifecycleAsync("branch", command with { RequestId = "close", Action = StoreLifecycleAction.CloseStore, CashAmount = 490 });
        var parent = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "parent", Now, TimeSpan.FromMinutes(1)));
        await store.ConfirmClosingReportAsync(parent, Now);
        return (store, parent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streamed_acknowledgement_reads_at_most_limit_plus_one_even_when_length_is_untrusted(bool declaredShort)
    {
        using var stream = new CountedStream(Encoding.UTF8.GetBytes(new string(' ', 20000)));
        using var handler = new Handler((_, _) =>
        {
            var content = new StreamContent(stream);
            content.Headers.ContentLength = declaredShort ? 2 : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        Assert.Equal(ClosingLifecycleFailure.AcknowledgmentUnavailable, await Transport(handler).SendAsync(Message, default));
        Assert.Equal(HttpClosingLifecycleTransport.MaximumAcknowledgementBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task Deadline_applies_to_stalled_response_body_after_headers_arrive()
    {
        using var stream = new StalledStream();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        Assert.Equal(ClosingLifecycleFailure.TransportUnavailable,
            await Transport(handler, TimeSpan.FromSeconds(1)).SendAsync(Message, default));
        Assert.True(stream.ReadStarted);
    }
    private static IOptions<ClosingReportDeliveryOptions> Settings(bool enabled = true) => Options.Create(new ClosingReportDeliveryOptions
    {
        Enabled = true,
        LifecycleEnabled = enabled,
        BranchId = "branch",
        RetryDelay = TimeSpan.FromSeconds(10),
        MaximumRetryDelay = TimeSpan.FromSeconds(20)
    });

    [Fact]
    public async Task Parent_missing_replays_exact_original_once_then_confirms_context_without_reclosing()
    {
        var (store, parent) = await Closed(); var clock = new Clock(); var sends = 0; var replays = 0;
        var transport = new ContextTransport((_, _) => Task.FromResult<ClosingLifecycleFailure?>(++sends == 1 ? ClosingLifecycleFailure.ParentUnavailable : null));
        var repair = new ParentTransport((message, _) =>
        {
            replays++; Assert.Equal(parent.PayloadJson, message.PayloadJson); Assert.Equal(parent.ReportId, message.ReportId);
            return Task.FromResult<ClosingReportDeliveryFailure?>(null);
        });
        Assert.True(await new ClosingLifecycleDeliveryCycle(store, transport, repair, clock, Settings()).RunOnceAsync());
        Assert.Equal(2, sends); Assert.Equal(1, replays);
        var detail = await store.ReadClosingReportDetailAsync("branch", "close", Now);
        Assert.Equal("Delivered", detail!.LifecycleDelivery!.DeliveryState);
        Assert.Equal(Now, detail.Report.DeliveredAt);
        Assert.Equal(1, detail.Report.AttemptCount);
        Assert.Equal("Current", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now)).State);
    }

    [Fact]
    public async Task Lost_context_acknowledgement_survives_restart_and_does_not_hide_behind_parent_receipt()
    {
        var (store, _) = await Closed(); var clock = new Clock(); var bodies = new List<string>();
        var failed = new ContextTransport((message, _) => { bodies.Add(message.PayloadJson); return Task.FromResult<ClosingLifecycleFailure?>(ClosingLifecycleFailure.AcknowledgmentUnavailable); });
        var parent = new ParentTransport((_, _) => throw new InvalidOperationException("No repair expected"));
        Assert.True(await new ClosingLifecycleDeliveryCycle(store, failed, parent, clock, Settings()).RunOnceAsync());
        Assert.Equal("Pending", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now)).State);
        var pending = await store.ReadClosingReportDetailAsync("branch", "close", Now);
        Assert.Equal("RetryScheduled", pending!.LifecycleDelivery!.DeliveryState);
        Assert.Equal("Delivered", pending.Report.DeliveryState);
        clock.Utc = Now.AddSeconds(10);
        var reopened = new SqliteTableSessionCommandStore(_path);
        var accepted = new ContextTransport((message, _) => { bodies.Add(message.PayloadJson); return Task.FromResult<ClosingLifecycleFailure?>(null); });
        Assert.True(await new ClosingLifecycleDeliveryCycle(reopened, accepted, parent, clock, Settings()).RunOnceAsync());
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal("Delivered", (await reopened.ReadClosingReportDetailAsync("branch", "close", clock.Utc))!.LifecycleDelivery!.DeliveryState);
    }

    [Fact]
    public async Task Repair_failure_is_bounded_and_retains_pending_context()
    {
        var (store, _) = await Closed(); var sends = 0; var repairs = 0;
        var missing = new ContextTransport((_, _) => { sends++; return Task.FromResult<ClosingLifecycleFailure?>(ClosingLifecycleFailure.ParentUnavailable); });
        var rejected = new ParentTransport((_, _) => { repairs++; return Task.FromResult<ClosingReportDeliveryFailure?>(ClosingReportDeliveryFailure.CentralRejected); });
        Assert.True(await new ClosingLifecycleDeliveryCycle(store, missing, rejected, new Clock(), Settings()).RunOnceAsync());
        Assert.Equal(1, sends); Assert.Equal(1, repairs);
        Assert.Equal("ParentUnavailable", (await store.ReadClosingReportDetailAsync("branch", "close", Now))!.LifecycleDelivery!.LastFailure);
    }

    [Fact]
    public async Task Disabled_context_delivery_does_not_claim_or_send_jobs()
    {
        var (store, _) = await Closed();
        var transport = new ContextTransport((_, _) => throw new InvalidOperationException("Must remain disabled"));
        var parent = new ParentTransport((_, _) => throw new InvalidOperationException("Must not replay"));
        Assert.False(await new ClosingLifecycleDeliveryCycle(store, transport, parent, new Clock(), Settings(false)).RunOnceAsync());
        Assert.Equal(0, (await store.ReadClosingReportDetailAsync("branch", "close", Now))!.LifecycleDelivery!.AttemptCount);
    }

    [Fact]
    public async Task Context_backoff_caps_and_pause_resume_preserves_packet_and_parent_receipt()
    {
        var (store, _) = await Closed(); var clock = new Clock(); var bodies = new List<string>();
        var parent = new ParentTransport((_, _) => throw new InvalidOperationException("No parent repair expected"));
        var failing = new ContextTransport((lease, _) =>
        {
            bodies.Add(lease.PayloadJson);
            return Task.FromResult<ClosingLifecycleFailure?>(ClosingLifecycleFailure.TransportUnavailable);
        });
        var cycle = new ClosingLifecycleDeliveryCycle(store, failing, parent, clock, Settings());
        foreach (var second in new[] { 0, 10, 30, 50 })
        {
            clock.Utc = Now.AddSeconds(second);
            Assert.True(await cycle.RunOnceAsync());
            Assert.False(await cycle.RunOnceAsync());
        }
        var paused = (await store.ReadClosingReportDetailAsync("branch", "close", clock.Utc))!;
        Assert.Equal(4, paused.LifecycleDelivery!.AttemptCount);
        Assert.Equal(Now.AddSeconds(70), paused.LifecycleDelivery.AvailableAt);
        clock.Utc = Now.AddSeconds(90);
        Assert.False(await new ClosingLifecycleDeliveryCycle(store, failing, parent, clock, Settings(false)).RunOnceAsync());
        Assert.Equal("Pending", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", clock.Utc)).State);
        var reopened = new SqliteTableSessionCommandStore(_path);
        var accepted = new ContextTransport((lease, _) => { bodies.Add(lease.PayloadJson); return Task.FromResult<ClosingLifecycleFailure?>(null); });
        Assert.True(await new ClosingLifecycleDeliveryCycle(reopened, accepted, parent, clock, Settings()).RunOnceAsync());
        var completed = (await reopened.ReadClosingReportDetailAsync("branch", "close", clock.Utc))!;
        Assert.Equal(paused.Report with { LifecycleDelivery = null }, completed.Report with { LifecycleDelivery = null });
        Assert.Equal(completed.LifecycleDelivery, completed.Report.LifecycleDelivery);
        Assert.Equal("Delivered", completed.LifecycleDelivery!.DeliveryState);
        Assert.Equal(5, completed.LifecycleDelivery.AttemptCount);
        Assert.Null(completed.LifecycleDelivery.LastFailure);
        Assert.Equal(5, bodies.Count); Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Equal("Current", (await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", clock.Utc)).State);
    }

    private sealed class Clock : TimeProvider { internal DateTimeOffset Utc { get; set; } = Now; public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class CountedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        internal int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken); BytesRead += count; return count;
        }
    }
    private sealed class StalledStream : MemoryStream
    {
        public override bool CanSeek => false;
        internal bool ReadStarted { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class ContextTransport(Func<ClosingLifecycleLease, CancellationToken, Task<ClosingLifecycleFailure?>> send) : IClosingLifecycleTransport
    { public Task<ClosingLifecycleFailure?> SendAsync(ClosingLifecycleLease message, CancellationToken token) => send(message, token); }
    private sealed class ParentTransport(Func<ClosingReportLease, CancellationToken, Task<ClosingReportDeliveryFailure?>> send) : IClosingReportTransport
    { public Task<ClosingReportDeliveryFailure?> SendAsync(ClosingReportLease message, CancellationToken token) => send(message, token); }
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); } }
}
