using Pos.Application.Synchronization;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxDeliveryClientTests
{
    [Fact]
    public void Durable_acknowledgment_contains_no_error()
    {
        var result = BranchCloudOutboxDeliveryResult.Acknowledged(" EVENT-01 ", 7);

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
            result.Disposition);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.SuggestedRetryDelay);
        Assert.Null(result.ExpectedBranchSequence);
        Assert.Equal("EVENT-01", result.AcknowledgedEventId);
        Assert.Equal(7, result.AcknowledgedBranchSequence);
    }

    [Fact]
    public void Retryable_failure_normalizes_error_code()
    {
        var result = BranchCloudOutboxDeliveryResult.Failed(" CLOUD_TIMEOUT ");

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("CLOUD_TIMEOUT", result.ErrorCode);
        Assert.Null(result.SuggestedRetryDelay);
        Assert.Null(result.ExpectedBranchSequence);
    }

    [Fact]
    public void Retryable_failure_preserves_positive_retry_delay_hint()
    {
        var result = BranchCloudOutboxDeliveryResult.Failed(
            "CLOUD_BUSY",
            TimeSpan.FromSeconds(45));

        Assert.Equal(TimeSpan.FromSeconds(45), result.SuggestedRetryDelay);
    }

    [Fact]
    public void Retryable_failure_preserves_expected_branch_sequence()
    {
        var result = BranchCloudOutboxDeliveryResult.Failed(
            "BranchSequenceGap",
            expectedBranchSequence: 6);

        Assert.Equal(6, result.ExpectedBranchSequence);
    }

    [Fact]
    public void Durable_acknowledgment_rejects_error_code()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxDeliveryResult(
                BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
                "UNEXPECTED",
                "EVENT-01",
                7));

        Assert.Equal("errorCode", exception.ParamName);
    }

    [Fact]
    public void Durable_acknowledgment_rejects_retry_delay_hint()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxDeliveryResult(
                BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
                acknowledgedEventId: "EVENT-01",
                acknowledgedBranchSequence: 7,
                suggestedRetryDelay: TimeSpan.FromSeconds(5)));

        Assert.Equal("suggestedRetryDelay", exception.ParamName);
    }

    [Fact]
    public void Durable_acknowledgment_rejects_expected_branch_sequence()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxDeliveryResult(
                BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
                acknowledgedEventId: "EVENT-01",
                acknowledgedBranchSequence: 7,
                expectedBranchSequence: 6));

        Assert.Equal("expectedBranchSequence", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Durable_acknowledgment_requires_event_id(string? eventId)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BranchCloudOutboxDeliveryResult.Acknowledged(eventId!, 7));

        Assert.Equal("acknowledgedEventId", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Durable_acknowledgment_requires_positive_sequence(long sequence)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BranchCloudOutboxDeliveryResult.Acknowledged("EVENT-01", sequence));

        Assert.Equal("acknowledgedBranchSequence", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Retryable_failure_requires_error_code(string? errorCode)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxDeliveryResult(
                BranchCloudOutboxDeliveryDisposition.RetryableFailure,
                errorCode));

        Assert.Equal("errorCode", exception.ParamName);
    }

    [Fact]
    public void Retryable_failure_rejects_acknowledged_identity()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudOutboxDeliveryResult(
                BranchCloudOutboxDeliveryDisposition.RetryableFailure,
                "CLOUD_TIMEOUT",
                "EVENT-01",
                7));

        Assert.Equal("acknowledgedEventId", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Retryable_failure_requires_positive_retry_delay_hint(int seconds)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BranchCloudOutboxDeliveryResult.Failed(
                "CLOUD_BUSY",
                TimeSpan.FromSeconds(seconds)));

        Assert.Equal("suggestedRetryDelay", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Retryable_failure_requires_positive_expected_sequence(long sequence)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BranchCloudOutboxDeliveryResult.Failed(
                "BranchSequenceGap",
                expectedBranchSequence: sequence));

        Assert.Equal("expectedBranchSequence", exception.ParamName);
    }

    [Fact]
    public void Undefined_disposition_is_rejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchCloudOutboxDeliveryResult(
                (BranchCloudOutboxDeliveryDisposition)99,
                acknowledgedEventId: "EVENT-01",
                acknowledgedBranchSequence: 7));

        Assert.Equal("disposition", exception.ParamName);
    }
}
