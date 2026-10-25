namespace Pos.Application.Synchronization;

public enum BranchCloudOutboxDeliveryDisposition
{
    DurablyAcknowledged,
    RetryableFailure
}

public sealed class BranchCloudOutboxDeliveryResult
{
    public BranchCloudOutboxDeliveryResult(
        BranchCloudOutboxDeliveryDisposition disposition,
        string? errorCode = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(disposition),
                "The Cloud delivery disposition is not defined.");
        }

        if (disposition is BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged)
        {
            if (errorCode is not null)
            {
                throw new ArgumentException(
                    "A durable acknowledgment cannot contain an error code.",
                    nameof(errorCode));
            }

            Disposition = disposition;
            return;
        }

        ErrorCode = RequireErrorCode(errorCode);
        Disposition = disposition;
    }

    public BranchCloudOutboxDeliveryDisposition Disposition { get; }

    public string? ErrorCode { get; }

    public static BranchCloudOutboxDeliveryResult Acknowledged() =>
        new(BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged);

    public static BranchCloudOutboxDeliveryResult Failed(string errorCode) =>
        new(BranchCloudOutboxDeliveryDisposition.RetryableFailure, errorCode);

    private static string RequireErrorCode(string? errorCode) =>
        string.IsNullOrWhiteSpace(errorCode)
            ? throw new ArgumentException(
                "A retryable Cloud delivery failure requires an error code.",
                nameof(errorCode))
            : errorCode.Trim();
}

public interface IBranchCloudOutboxDeliveryClient
{
    /// <summary>
    /// Delivers the leased outbox event identified by Branch, outbox ID, and
    /// Branch sequence. A durable acknowledgment may be returned only after
    /// Cloud has durably persisted that same event identity. Every other
    /// completed response must remain retryable.
    /// </summary>
    Task<BranchCloudOutboxDeliveryResult> DeliverAsync(
        BranchCloudOutboxLease lease,
        CancellationToken cancellationToken = default);
}
