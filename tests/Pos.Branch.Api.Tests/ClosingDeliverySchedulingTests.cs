using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class ClosingDeliverySchedulingTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"closing-scheduling-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(1);
    private int _shift;
    private static IOptions<ClosingReportDeliveryOptions> Settings(bool lifecycle = true) => Options.Create(new ClosingReportDeliveryOptions
    {
        Enabled = true,
        LifecycleEnabled = lifecycle,
        BranchId = "branch",
        LeaseDuration = Duration,
        RetryDelay = TimeSpan.FromSeconds(10),
        MaximumRetryDelay = TimeSpan.FromSeconds(20)
    });
    private static StoreLifecycleCommand Command(string id, string shift, StoreLifecycleAction action, decimal? cash = null) =>
        new(id, "store", shift, action, cash, new("employee", "device", Now, new(2026, 11, 22)));
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", "0", StoreLifecycleAction.OpenStore, 500));
        return store;
    }
    private async Task<string> Close(SqliteTableSessionCommandStore store)
    {
        var request = $"close-{_shift}";
        await store.ExecuteStoreLifecycleAsync("branch", Command(request, _shift.ToString(), StoreLifecycleAction.CloseShift));
        _shift++;
        await store.ExecuteStoreLifecycleAsync("branch", Command($"open-{_shift}", _shift.ToString(), StoreLifecycleAction.OpenShift));
        return request;
    }
    private static ClosingReportDeliveryCycle Cycle(SqliteTableSessionCommandStore store, Parent parent, Context context,
        Clock clock, bool enabled = true) => new(store, parent, clock, Settings(enabled),
            new ClosingLifecycleDeliveryCycle(store, context, parent, clock, Settings(enabled)));
    private static async Task AcknowledgeParent(SqliteTableSessionCommandStore store)
    {
        var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "seed", Now, Duration));
        Assert.True(await store.ConfirmClosingReportAsync(lease, Now));
    }

    [Fact]
    public async Task Sustained_base_backlog_gives_each_eligible_context_a_bounded_turn_without_parallel_sends()
    {
        var store = await Store(); await Close(store); await Close(store);
        var clock = new Clock(); var trace = new List<string>(); var active = 0;
        var parent = new Parent(async (lease, _) =>
        {
            Assert.Equal(0, active++); await Task.Yield(); trace.Add("base:" + lease.RequestId); active--;
            return null;
        });
        var context = new Context(async (lease, _) =>
        {
            Assert.Equal(0, active++); await Task.Yield(); trace.Add("context:" + lease.RequestId); active--;
            var report = await store.ReadClosingReportDetailAsync("branch", lease.RequestId, clock.Utc);
            Assert.Equal("Delivered", report!.Report.DeliveryState);
            return null;
        });
        var cycle = Cycle(store, parent, context, clock);
        for (var index = 0; index < 5; index++)
        {
            await Close(store);
            Assert.True(await cycle.RunOnceAsync());
            var overview = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now);
            Assert.Equal(2, overview.ClosingReports.PendingCount);
            Assert.Equal(index + 1, overview.LifecycleReports!.DeliveredCount);
            Assert.Equal(2, overview.LifecycleReports.PendingCount);
        }
        Assert.Equal(Enumerable.Range(0, 5).SelectMany(index => new[] { $"base:close-{index}", $"context:close-{index}" }), trace);
    }

    [Fact]
    public async Task Failed_base_does_not_block_an_older_context_with_its_own_confirmed_parent()
    {
        var store = await Store(); var older = await Close(store); await AcknowledgeParent(store);
        var newer = await Close(store); var trace = new List<string>();
        var parent = new Parent((lease, _) => { trace.Add("base:" + lease.RequestId); return Task.FromResult<ClosingReportDeliveryFailure?>(ClosingReportDeliveryFailure.TransportUnavailable); });
        var context = new Context((lease, _) => { trace.Add("context:" + lease.RequestId); return Task.FromResult<ClosingLifecycleFailure?>(null); });
        Assert.True(await Cycle(store, parent, context, new Clock()).RunOnceAsync());
        Assert.Equal(new[] { "base:" + newer, "context:" + older }, trace);
        var oldDetail = await store.ReadClosingReportDetailAsync("branch", older, Now);
        var newDetail = await store.ReadClosingReportDetailAsync("branch", newer, Now);
        Assert.Equal("Delivered", oldDetail!.LifecycleDelivery!.DeliveryState);
        Assert.Equal("RetryScheduled", newDetail!.Report.DeliveryState);
        Assert.Equal(0, newDetail.LifecycleDelivery!.AttemptCount);
    }

    [Fact]
    public async Task Failed_parent_cannot_release_its_unconfirmed_context_and_delayed_work_becomes_idle()
    {
        var store = await Store(); var request = await Close(store); var baseSends = 0;
        var parent = new Parent((_, _) => { baseSends++; return Task.FromResult<ClosingReportDeliveryFailure?>(ClosingReportDeliveryFailure.TransportUnavailable); });
        var context = new Context((_, _) => throw new InvalidOperationException("Unconfirmed parent cannot release companion"));
        var cycle = Cycle(store, parent, context, new Clock());
        Assert.True(await cycle.RunOnceAsync());
        Assert.False(await cycle.RunOnceAsync());
        Assert.Equal(1, baseSends);
        Assert.Equal(0, (await store.ReadClosingReportDetailAsync("branch", request, Now))!.LifecycleDelivery!.AttemptCount);
    }

    [Fact]
    public async Task Context_failure_preserves_the_just_confirmed_base_receipt_and_restarts_exact_companion()
    {
        var store = await Store(); var request = await Close(store); var clock = new Clock(); var bodies = new List<string>();
        var parent = new Parent((_, _) => Task.FromResult<ClosingReportDeliveryFailure?>(null));
        var context = new Context((lease, _) => { bodies.Add(lease.PayloadJson); return Task.FromResult<ClosingLifecycleFailure?>(ClosingLifecycleFailure.AcknowledgmentUnavailable); });
        Assert.True(await Cycle(store, parent, context, clock).RunOnceAsync());
        var before = await store.ReadClosingReportDetailAsync("branch", request, Now);
        Assert.Equal("Delivered", before!.Report.DeliveryState); Assert.Equal("RetryScheduled", before.LifecycleDelivery!.DeliveryState);
        var snapshot = System.Text.Json.JsonSerializer.Serialize(before.Lifecycle);
        clock.Utc = Now.AddSeconds(10);
        var reopened = new SqliteTableSessionCommandStore(_path);
        var accepted = new Context((lease, _) => { bodies.Add(lease.PayloadJson); return Task.FromResult<ClosingLifecycleFailure?>(null); });
        var noReplay = new Parent((_, _) => throw new InvalidOperationException("Confirmed base must not resend"));
        Assert.True(await Cycle(reopened, noReplay, accepted, clock).RunOnceAsync());
        var after = await reopened.ReadClosingReportDetailAsync("branch", request, clock.Utc);
        Assert.Equal("Delivered", after!.LifecycleDelivery!.DeliveryState);
        Assert.Equal(before.Report, after.Report);
        Assert.Equal(snapshot, System.Text.Json.JsonSerializer.Serialize(after.Lifecycle));
        Assert.Equal(bodies[0], bodies[1]);
        Assert.False(await Cycle(reopened, noReplay, accepted, clock).RunOnceAsync());
    }

    [Fact]
    public async Task Shutdown_during_context_send_keeps_base_receipt_and_recovers_companion_after_lease_expiry()
    {
        var store = await Store(); var request = await Close(store); var clock = new Clock();
        using var shutdown = new CancellationTokenSource();
        var parent = new Parent((_, _) => Task.FromResult<ClosingReportDeliveryFailure?>(null));
        ClosingLifecycleLease? interrupted = null;
        var context = new Context((lease, token) =>
        {
            interrupted = lease; shutdown.Cancel(); token.ThrowIfCancellationRequested();
            return Task.FromResult<ClosingLifecycleFailure?>(null);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cycle(store, parent, context, clock).RunOnceAsync(shutdown.Token));
        var pending = await store.ReadClosingReportDetailAsync("branch", request, Now);
        Assert.Equal("Delivered", pending!.Report.DeliveryState); Assert.Equal("InFlight", pending.LifecycleDelivery!.DeliveryState);
        clock.Utc = Now.Add(Duration);
        var reopened = new SqliteTableSessionCommandStore(_path);
        var accepted = new Context((lease, _) =>
        {
            Assert.Equal(interrupted!.PayloadJson, lease.PayloadJson);
            Assert.NotEqual(interrupted.LeaseId, lease.LeaseId); Assert.Equal(2, lease.AttemptCount);
            return Task.FromResult<ClosingLifecycleFailure?>(null);
        });
        Assert.True(await Cycle(reopened, parent, accepted, clock).RunOnceAsync());
        Assert.False(await reopened.ConfirmClosingLifecycleAsync(interrupted!, clock.Utc));
        Assert.Equal(pending.Report, (await reopened.ReadClosingReportDetailAsync("branch", request, clock.Utc))!.Report);
    }

    [Fact]
    public async Task Base_only_and_disabled_modes_keep_companion_unclaimed_and_empty_queues_return_false()
    {
        var store = await Store(); var request = await Close(store); var clock = new Clock();
        var parent = new Parent((_, _) => Task.FromResult<ClosingReportDeliveryFailure?>(null));
        var forbidden = new Context((_, _) => throw new InvalidOperationException("Disabled context cannot send"));
        var baseOnly = new ClosingReportDeliveryCycle(store, parent, clock, Settings(false));
        Assert.True(await baseOnly.RunOnceAsync()); Assert.False(await baseOnly.RunOnceAsync());
        Assert.False(await Cycle(store, parent, forbidden, clock, false).RunOnceAsync());
        Assert.Equal(0, (await store.ReadClosingReportDetailAsync("branch", request, Now))!.LifecycleDelivery!.AttemptCount);
    }

    private sealed class Clock : TimeProvider { internal DateTimeOffset Utc { get; set; } = Now; public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Parent(Func<ClosingReportLease, CancellationToken, Task<ClosingReportDeliveryFailure?>> send) : IClosingReportTransport
    { public Task<ClosingReportDeliveryFailure?> SendAsync(ClosingReportLease lease, CancellationToken token) => send(lease, token); }
    private sealed class Context(Func<ClosingLifecycleLease, CancellationToken, Task<ClosingLifecycleFailure?>> send) : IClosingLifecycleTransport
    { public Task<ClosingLifecycleFailure?> SendAsync(ClosingLifecycleLease lease, CancellationToken token) => send(lease, token); }
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); } }
}
