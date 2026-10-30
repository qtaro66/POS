using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxSequenceReplayFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Failure_preserves_recovery_context_and_retry_guidance()
    {
        var lease = Lease();
        var failure = new BranchCloudOutboxSequenceReplayFailure(
            " FAILURE-01 ", lease, Now.AddSeconds(5), Now.AddMinutes(1), " BranchSequenceGap ", 5);

        Assert.Same(lease, failure.Lease);
        Assert.Equal("FAILURE-01", failure.FailureId);
        Assert.Equal("BranchSequenceGap", failure.ErrorCode);
        Assert.Equal(5, failure.ExpectedBranchSequence);
        Assert.Equal(Now.AddSeconds(5), failure.FailedAt);
        Assert.Equal(Now.AddMinutes(1), failure.NextRetryAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    [InlineData(31)]
    public void Failure_rejects_time_outside_active_lease(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchCloudOutboxSequenceReplayFailure(
            "F", Lease(), Now.AddSeconds(seconds), Now.AddMinutes(2), "Offline"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Failure_requires_future_retry(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchCloudOutboxSequenceReplayFailure(
            "F", Lease(), Now, Now.AddSeconds(seconds), "Offline"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(7)]
    public void Failure_rejects_invalid_gap_sequence(long sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchCloudOutboxSequenceReplayFailure(
            "F", Lease(), Now, Now.AddSeconds(5), "BranchSequenceGap", sequence));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failure_rejects_non_UTC_times(bool failureTime)
    {
        var local = Now.ToOffset(TimeSpan.FromHours(7));
        Assert.Throws<ArgumentException>(() => new BranchCloudOutboxSequenceReplayFailure(
            "F", Lease(), failureTime ? local : Now,
            failureTime ? Now.AddMinutes(1) : local.AddMinutes(1), "Offline"));
    }

    [Theory]
    [InlineData("", "Offline")]
    [InlineData(" ", "Offline")]
    [InlineData("F", "")]
    [InlineData("F", " ")]
    public void Failure_rejects_empty_identifiers(string failureId, string errorCode)
    {
        Assert.Throws<ArgumentException>(() => new BranchCloudOutboxSequenceReplayFailure(
            failureId, Lease(), Now, Now.AddMinutes(1), errorCode));
    }

    [Fact]
    public void Failure_rejects_missing_lease()
    {
        Assert.Throws<ArgumentNullException>(() => new BranchCloudOutboxSequenceReplayFailure(
            "F", null!, Now, Now.AddMinutes(1), "Offline"));
    }

    private static BranchCloudOutboxSequenceReplayLease Lease() =>
        new("RECOVERY-01", "OUTBOX-06", "BRANCH-01", 6, "REQUEST-06",
            TableSessionId.From("SESSION-06"), TableSessionAction.SubmitOrder,
            3, Now.AddMinutes(-5), 2, "WORKER-01", Now, Now.AddSeconds(30));
}
