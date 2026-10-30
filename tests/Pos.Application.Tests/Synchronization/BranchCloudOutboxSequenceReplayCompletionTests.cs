using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxSequenceReplayCompletionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completion_preserves_lease_and_matching_acknowledgment_time()
    {
        var lease = Lease();
        var completion = new BranchCloudOutboxSequenceReplayCompletion(
            lease, "OUTBOX-06", 6, Now.AddSeconds(10));

        Assert.Same(lease, completion.Lease);
        Assert.Equal(Now.AddSeconds(10), completion.AcknowledgedAt);
    }

    [Theory]
    [InlineData("OTHER")]
    [InlineData("outbox-06")]
    [InlineData("")]
    [InlineData(null)]
    public void Completion_rejects_mismatched_event(string? eventId)
    {
        Assert.Throws<ArgumentException>(() => new BranchCloudOutboxSequenceReplayCompletion(
            Lease(), eventId!, 6, Now.AddSeconds(10)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(7)]
    public void Completion_rejects_mismatched_sequence(long sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchCloudOutboxSequenceReplayCompletion(
            Lease(), "OUTBOX-06", sequence, Now.AddSeconds(10)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    [InlineData(31)]
    public void Completion_rejects_time_outside_active_window(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchCloudOutboxSequenceReplayCompletion(
            Lease(), "OUTBOX-06", 6, Now.AddSeconds(seconds)));
    }

    [Fact]
    public void Completion_rejects_non_UTC_time()
    {
        Assert.Throws<ArgumentException>(() => new BranchCloudOutboxSequenceReplayCompletion(
            Lease(), "OUTBOX-06", 6, Now.ToOffset(TimeSpan.FromHours(7))));
    }

    [Fact]
    public void Completion_rejects_missing_lease()
    {
        Assert.Throws<ArgumentNullException>(() => new BranchCloudOutboxSequenceReplayCompletion(
            null!, "OUTBOX-06", 6, Now));
    }

    private static BranchCloudOutboxSequenceReplayLease Lease() =>
        new("RECOVERY-01", "OUTBOX-06", "BRANCH-01", 6, "REQUEST-06",
            TableSessionId.From("SESSION-06"), TableSessionAction.SubmitOrder,
            3, Now.AddMinutes(-5), 0, "WORKER-01", Now, Now.AddSeconds(30));
}
