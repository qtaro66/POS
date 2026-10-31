using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudReplayLeaseHeartbeatTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Heartbeat_renews_and_returns_latest_token_after_stopping()
    {
        var store = new RenewalStore();
        var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        TimeSpan observedInterval = default;
        Task Delay(TimeSpan interval, CancellationToken token)
        {
            observedInterval = interval;
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                return tick.Task.WaitAsync(token);
            }
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        await using var heartbeat = new BranchCloudReplayLeaseHeartbeat(
            store, Lease(), TimeSpan.FromSeconds(30), new Clock(), delay: Delay);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        tick.SetResult();
        await store.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var latest = await heartbeat.StopAsync();

        Assert.Equal(TimeSpan.FromSeconds(10), observedInterval);
        Assert.Equal(Now.AddSeconds(40), latest.LeaseExpiresAt);
        Assert.Equal("RECOVERY-01", latest.RecoveryId);
        Assert.Equal(1, store.Calls);
        Assert.False(heartbeat.OperationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Renewal_failure_cancels_delivery_and_surfaces_cause()
    {
        var store = new RenewalStore { Fail = true };
        var heartbeat = new BranchCloudReplayLeaseHeartbeat(
            store, Lease(), TimeSpan.FromSeconds(30), new Clock(),
            delay: (_, _) => Task.CompletedTask);

        Assert.True(heartbeat.OperationToken.IsCancellationRequested);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => heartbeat.StopAsync());
        Assert.IsType<IOException>(failure.InnerException);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await heartbeat.DisposeAsync());
    }

    [Fact]
    public async Task Caller_cancellation_stops_heartbeat_without_renewal()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RenewalStore();
        await using var heartbeat = new BranchCloudReplayLeaseHeartbeat(
            store, Lease(), TimeSpan.FromSeconds(30), new Clock(), cancellation.Token,
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        cancellation.Cancel();

        var latest = await heartbeat.StopAsync();

        Assert.True(heartbeat.OperationToken.IsCancellationRequested);
        Assert.Equal(0, store.Calls);
        Assert.Equal(Lease().LeaseExpiresAt, latest.LeaseExpiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_duration_is_rejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchCloudReplayLeaseHeartbeat(
            new RenewalStore(), Lease(), TimeSpan.FromSeconds(seconds), new Clock()));
    }

    private static BranchCloudOutboxSequenceReplayLease Lease() =>
        new("RECOVERY-01", "EVENT-01", "BRANCH-01", 6, "REQUEST-01",
            TableSessionId.From("SESSION-01"), TableSessionAction.SubmitOrder,
            3, Now.AddMinutes(-5), 0, "WORKER-01", Now, Now.AddSeconds(30));

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now.AddSeconds(10);
    }

    private sealed class RenewalStore : IBranchCloudOutboxSequenceReplayLeaseRenewalStore
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail { get; init; }
        public int Calls { get; private set; }
        public Task<BranchCloudOutboxSequenceReplayLease> RenewAsync(
            BranchCloudOutboxSequenceReplayLease lease, DateTimeOffset renewedAt,
            TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) { throw new IOException("simulated renewal failure"); }
            var renewed = lease.Renew(renewedAt, leaseDuration);
            Called.TrySetResult();
            return Task.FromResult(renewed);
        }
    }
}
