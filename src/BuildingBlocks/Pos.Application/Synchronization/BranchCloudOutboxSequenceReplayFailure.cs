namespace Pos.Application.Synchronization;

public sealed class BranchCloudOutboxSequenceReplayFailure
{
    public BranchCloudOutboxSequenceReplayFailure(
        string failureId,
        BranchCloudOutboxSequenceReplayLease lease,
        DateTimeOffset failedAt,
        DateTimeOffset nextRetryAt,
        string errorCode,
        long? expectedBranchSequence = null)
    {
        FailureId = RequireIdentifier(failureId, nameof(failureId));
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        FailedAt = RequireUtc(failedAt, nameof(failedAt));
        NextRetryAt = RequireUtc(nextRetryAt, nameof(nextRetryAt));
        ErrorCode = RequireIdentifier(errorCode, nameof(errorCode));
        ExpectedBranchSequence = expectedBranchSequence switch
        {
            null => null,
            >= 1 when expectedBranchSequence < lease.BranchSequence => expectedBranchSequence,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedBranchSequence))
        };
        if (FailedAt < lease.ClaimedAt || FailedAt >= lease.LeaseExpiresAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failedAt), "Failure must occur inside the active replay lease window.");
        }
        if (NextRetryAt <= FailedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextRetryAt), "The next retry must be later than the failure.");
        }
    }

    public string FailureId { get; }

    public BranchCloudOutboxSequenceReplayLease Lease { get; }

    public DateTimeOffset FailedAt { get; }

    public DateTimeOffset NextRetryAt { get; }

    public string ErrorCode { get; }

    public long? ExpectedBranchSequence { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("The timestamp must be UTC.", parameterName);
}

public interface IBranchCloudOutboxSequenceReplayFailureStore
{
    /// <summary>
    /// Atomically records a failure against exact active replay ownership,
    /// increments replay retry count once, schedules retry, and releases the
    /// replay lease. Same failure ID and data return the original result;
    /// conflicting ID reuse or stale/completed ownership fails closed.
    /// Original event acknowledgment and regular outbox retry state are preserved.
    /// </summary>
    Task<BranchCloudOutboxDeliveryFailureResult> RecordAsync(
        BranchCloudOutboxSequenceReplayFailure failure,
        CancellationToken cancellationToken = default);
}
