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
