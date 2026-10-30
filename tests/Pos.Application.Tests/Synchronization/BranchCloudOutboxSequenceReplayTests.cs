using Pos.Application.Synchronization;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxSequenceReplayTests
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 10, 30, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Request_preserves_normalized_recovery_identity_and_sequence_gap()
    {
        var request = new BranchCloudOutboxSequenceReplayRequest(
            " RECOVERY-01 ",
            " BRANCH-01 ",
            6,
            8,
            RequestedAt);

        Assert.Equal("RECOVERY-01", request.RecoveryId);
        Assert.Equal("BRANCH-01", request.BranchId);
        Assert.Equal(6, request.ExpectedBranchSequence);
        Assert.Equal(8, request.ReceivedBranchSequence);
        Assert.Equal(RequestedAt, request.RequestedAt);
    }

    [Theory]
    [InlineData("", "BRANCH-01", 6, 8, "recoveryId")]
    [InlineData("RECOVERY-01", " ", 6, 8, "branchId")]
    [InlineData("RECOVERY-01", "BRANCH-01", 0, 8, "expectedBranchSequence")]
    [InlineData("RECOVERY-01", "BRANCH-01", -1, 8, "expectedBranchSequence")]
    [InlineData("RECOVERY-01", "BRANCH-01", 6, 6, "receivedBranchSequence")]
    [InlineData("RECOVERY-01", "BRANCH-01", 6, 5, "receivedBranchSequence")]
    public void Invalid_identity_or_sequence_gap_is_rejected(
        string recoveryId,
        string branchId,
        long expectedBranchSequence,
        long receivedBranchSequence,
        string parameterName)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new BranchCloudOutboxSequenceReplayRequest(
                recoveryId,
                branchId,
                expectedBranchSequence,
                receivedBranchSequence,
                RequestedAt));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Non_utc_request_time_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxSequenceReplayRequest(
                "RECOVERY-01",
                "BRANCH-01",
                6,
                8,
                RequestedAt.ToOffset(TimeSpan.FromHours(7))));

        Assert.Equal("requestedAt", exception.ParamName);
    }

    [Theory]
    [InlineData(BranchCloudOutboxSequenceReplayResult.Requested)]
    [InlineData(BranchCloudOutboxSequenceReplayResult.AlreadyRequested)]
    [InlineData(BranchCloudOutboxSequenceReplayResult.ExpectedSequenceNotFound)]
    public void Result_dispositions_are_defined(
        BranchCloudOutboxSequenceReplayResult result)
    {
        Assert.True(Enum.IsDefined(result));
    }
}
