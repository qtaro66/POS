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
        string? errorCode = null,
        string? acknowledgedEventId = null,
        long? acknowledgedBranchSequence = null,
        TimeSpan? suggestedRetryDelay = null)
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

            if (suggestedRetryDelay is not null)
            {
                throw new ArgumentException(
                    "A durable acknowledgment cannot contain a retry-delay hint.",
                    nameof(suggestedRetryDelay));
            }

            AcknowledgedEventId = RequireIdentifier(
                acknowledgedEventId,
                nameof(acknowledgedEventId));
            AcknowledgedBranchSequence = acknowledgedBranchSequence is >= 1
                ? acknowledgedBranchSequence
                : throw new ArgumentOutOfRangeException(
                    nameof(acknowledgedBranchSequence),
                    "The acknowledged Branch sequence must be positive.");
            Disposition = disposition;
            return;
        }

        if (acknowledgedEventId is not null || acknowledgedBranchSequence is not null)
        {
            throw new ArgumentException(
                "A retryable failure cannot contain an acknowledged event identity.",
                nameof(acknowledgedEventId));
        }

        ErrorCode = RequireErrorCode(errorCode);
        SuggestedRetryDelay = suggestedRetryDelay switch
        {
            null => null,
            { } delay when delay > TimeSpan.Zero => delay,
            _ => throw new ArgumentOutOfRangeException(
                nameof(suggestedRetryDelay),
                "A retry-delay hint must be greater than zero.")
        };
        Disposition = disposition;
    }

    public BranchCloudOutboxDeliveryDisposition Disposition { get; }

    public string? ErrorCode { get; }

    public string? AcknowledgedEventId { get; }

    public long? AcknowledgedBranchSequence { get; }

    public TimeSpan? SuggestedRetryDelay { get; }

    public static BranchCloudOutboxDeliveryResult Acknowledged(
        string eventId,
        long branchSequence) =>
        new(
            BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
            acknowledgedEventId: eventId,
            acknowledgedBranchSequence: branchSequence);

    public static BranchCloudOutboxDeliveryResult Failed(
        string errorCode,
        TimeSpan? suggestedRetryDelay = null) =>
        new(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            errorCode,
            suggestedRetryDelay: suggestedRetryDelay);

    private static string RequireErrorCode(string? errorCode) =>
        string.IsNullOrWhiteSpace(errorCode)
            ? throw new ArgumentException(
                "A retryable Cloud delivery failure requires an error code.",
                nameof(errorCode))
            : errorCode.Trim();

    private static string RequireIdentifier(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The acknowledged event identifier cannot be empty.",
                parameterName)
            : value.Trim();
}

public interface IBranchCloudOutboxDeliveryClient
{
    /// <summary>
    /// Delivers the immutable outbox event identified by Branch, event ID, and
    /// Branch sequence. A durable acknowledgment may be returned only after
    /// Cloud has durably persisted that same event identity. Every other
    /// completed response must remain retryable. Local lease and retry state
    /// never crosses this boundary.
    /// </summary>
    Task<BranchCloudOutboxDeliveryResult> DeliverAsync(
        BranchCloudOutboxEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}
