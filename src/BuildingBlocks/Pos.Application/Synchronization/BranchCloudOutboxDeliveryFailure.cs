namespace Pos.Application.Synchronization;

public sealed class BranchCloudOutboxDeliveryFailure
{
    public BranchCloudOutboxDeliveryFailure(
        string failureId,
        BranchCloudOutboxLease lease,
        DateTimeOffset failedAt,
        DateTimeOffset nextRetryAt,
        string errorCode)
    {
        FailureId = RequireIdentifier(failureId, nameof(failureId));
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        FailedAt = RequireUtc(failedAt, nameof(failedAt));
        NextRetryAt = RequireUtc(nextRetryAt, nameof(nextRetryAt));
        ErrorCode = RequireIdentifier(errorCode, nameof(errorCode));

        if (FailedAt < Lease.ClaimedAt || FailedAt >= Lease.LeaseExpiresAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failedAt),
                "The failure time must fall within the active lease window.");
        }

        if (NextRetryAt <= FailedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextRetryAt),
                "The next retry time must be later than the failure time.");
        }
    }

    public string FailureId { get; }

    public BranchCloudOutboxLease Lease { get; }

    public DateTimeOffset FailedAt { get; }

    public DateTimeOffset NextRetryAt { get; }

    public string ErrorCode { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The identifier cannot be empty.",
                parameterName)
            : value.Trim();

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException(
                "The timestamp must be UTC.",
                parameterName);
}

public sealed class BranchCloudOutboxDeliveryFailureResult
{
    public BranchCloudOutboxDeliveryFailureResult(
        int retryCount,
        bool alreadyRecorded)
    {
        RetryCount = retryCount >= 1
            ? retryCount
            : throw new ArgumentOutOfRangeException(
                nameof(retryCount),
                "The retry count must be positive after a recorded failure.");
        AlreadyRecorded = alreadyRecorded;
    }

    public int RetryCount { get; }

    public bool AlreadyRecorded { get; }
}

public interface IBranchCloudOutboxDeliveryFailureStore
{
    /// <summary>
    /// Records a known delivery failure against the exact active lease,
    /// increments retry count once, schedules the next retry, and releases the
    /// lease atomically. Repeating the same failure identifier must return the
    /// original result without incrementing retry count again.
    /// </summary>
    Task<BranchCloudOutboxDeliveryFailureResult> RecordAsync(
        BranchCloudOutboxDeliveryFailure failure,
        CancellationToken cancellationToken = default);
}
