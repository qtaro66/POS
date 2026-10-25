using Pos.Application.Synchronization;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxDeliveryClientTests
{
    [Fact]
    public void Durable_acknowledgment_contains_no_error()
    {
        var result = BranchCloudOutboxDeliveryResult.Acknowledged();

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
            result.Disposition);
        Assert.Null(result.ErrorCode);
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
                "UNEXPECTED"));

        Assert.Equal("errorCode", exception.ParamName);
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
    public void Undefined_disposition_is_rejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchCloudOutboxDeliveryResult(
                (BranchCloudOutboxDeliveryDisposition)99));

        Assert.Equal("disposition", exception.ParamName);
    }
}
