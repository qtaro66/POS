using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxDeliveryFailureTests
{
    private static readonly DateTimeOffset ClaimedAt =
        new(2026, 10, 24, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Captures_normalized_failure_retry_and_exact_lease()
    {
        var lease = Lease();
        var failure = new BranchCloudOutboxDeliveryFailure(
            " FAILURE-01 ",
            lease,
            ClaimedAt.AddSeconds(10),
            ClaimedAt.AddMinutes(1),
            " CLOUD-UNAVAILABLE ");

        Assert.Equal("FAILURE-01", failure.FailureId);
        Assert.Same(lease, failure.Lease);
        Assert.Equal(ClaimedAt.AddSeconds(10), failure.FailedAt);
        Assert.Equal(ClaimedAt.AddMinutes(1), failure.NextRetryAt);
        Assert.Equal("CLOUD-UNAVAILABLE", failure.ErrorCode);
        Assert.Null(failure.ExpectedBranchSequence);
    }

    [Fact]
    public void Captures_expected_sequence_before_failed_sequence()
    {
        var failure = new BranchCloudOutboxDeliveryFailure(
            "FAILURE-01",
            Lease(),
            ClaimedAt.AddSeconds(10),
            ClaimedAt.AddMinutes(1),
            "BranchSequenceGap",
            expectedBranchSequence: 6);

        Assert.Equal(6, failure.ExpectedBranchSequence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(8)]
    public void Invalid_expected_sequence_is_rejected(long expectedSequence)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchCloudOutboxDeliveryFailure(
                "FAILURE-01",
                Lease(),
                ClaimedAt.AddSeconds(10),
                ClaimedAt.AddMinutes(1),
                "BranchSequenceGap",
                expectedSequence));

        Assert.Equal("expectedBranchSequence", exception.ParamName);
    }

    [Theory]
    [InlineData(null, "CLOUD-UNAVAILABLE", "failureId")]
    [InlineData(" ", "CLOUD-UNAVAILABLE", "failureId")]
    [InlineData("FAILURE-01", null, "errorCode")]
    [InlineData("FAILURE-01", "", "errorCode")]
    public void Empty_identifier_is_rejected(
        string? failureId,
        string? errorCode,
        string expectedParameter)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxDeliveryFailure(
                failureId!,
                Lease(),
                ClaimedAt.AddSeconds(10),
                ClaimedAt.AddMinutes(1),
                errorCode!));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Fact]
    public void Missing_lease_is_rejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudOutboxDeliveryFailure(
                "FAILURE-01",
                null!,
                ClaimedAt.AddSeconds(10),
                ClaimedAt.AddMinutes(1),
                "CLOUD-UNAVAILABLE"));

        Assert.Equal("lease", exception.ParamName);
    }

    [Theory]
    [InlineData("failedAt")]
    [InlineData("nextRetryAt")]
    public void Non_utc_timestamp_is_rejected(string field)
    {
        var nonUtc = ClaimedAt.ToOffset(TimeSpan.FromHours(7));

        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxDeliveryFailure(
                "FAILURE-01",
                Lease(),
                field == "failedAt" ? nonUtc : ClaimedAt.AddSeconds(10),
                field == "nextRetryAt" ? nonUtc : ClaimedAt.AddMinutes(1),
                "CLOUD-UNAVAILABLE"));

        Assert.Equal(field, exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    [InlineData(31)]
    public void Failure_outside_active_lease_is_rejected(int secondsFromClaim)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchCloudOutboxDeliveryFailure(
                "FAILURE-01",
                Lease(),
                ClaimedAt.AddSeconds(secondsFromClaim),
                ClaimedAt.AddMinutes(1),
                "CLOUD-UNAVAILABLE"));

        Assert.Equal("failedAt", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_future_retry_is_rejected(int secondsAfterFailure)
    {
        var failedAt = ClaimedAt.AddSeconds(10);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchCloudOutboxDeliveryFailure(
                "FAILURE-01",
                Lease(),
                failedAt,
                failedAt.AddSeconds(secondsAfterFailure),
                "CLOUD-UNAVAILABLE"));

        Assert.Equal("nextRetryAt", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_result_retry_count_is_rejected(int retryCount)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchCloudOutboxDeliveryFailureResult(
                retryCount,
                alreadyRecorded: false));

        Assert.Equal("retryCount", exception.ParamName);
    }

    [Fact]
    public void Result_captures_retry_count_and_idempotent_replay()
    {
        var result = new BranchCloudOutboxDeliveryFailureResult(
            3,
            alreadyRecorded: true);

        Assert.Equal(3, result.RetryCount);
        Assert.True(result.AlreadyRecorded);
    }

    private static BranchCloudOutboxLease Lease() =>
        new(
            "OUTBOX-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            TableSessionId.From("SESSION-01"),
            TableSessionAction.SubmitOrder,
            3,
            ClaimedAt.AddMinutes(-1),
            2,
            "WORKER-01",
            ClaimedAt,
            ClaimedAt.AddSeconds(30));
}
