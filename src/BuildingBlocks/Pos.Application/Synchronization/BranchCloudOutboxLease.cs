using Pos.Domain.TableSessions;

namespace Pos.Application.Synchronization;

public sealed class BranchCloudOutboxLease
{
    public BranchCloudOutboxLease(
        string outboxId,
        string branchId,
        long branchSequence,
        string requestId,
        TableSessionId tableSessionId,
        TableSessionAction action,
        long tableVersion,
        DateTimeOffset createdAt,
        int retryCount,
        string leaseOwner,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseExpiresAt)
    {
        OutboxId = RequireIdentifier(outboxId, nameof(outboxId));
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        BranchSequence = branchSequence >= 1
            ? branchSequence
            : throw new ArgumentOutOfRangeException(
                nameof(branchSequence),
                "The Branch sequence must be positive.");
        RequestId = RequireIdentifier(requestId, nameof(requestId));
        TableSessionId = tableSessionId ??
            throw new ArgumentNullException(nameof(tableSessionId));
        Action = Enum.IsDefined(action)
            ? action
            : throw new ArgumentOutOfRangeException(
                nameof(action),
                "The table-session action is not defined.");
        TableVersion = tableVersion >= 1
            ? tableVersion
            : throw new ArgumentOutOfRangeException(
                nameof(tableVersion),
                "The table version must be positive.");
        CreatedAt = RequireUtc(createdAt, nameof(createdAt));
        RetryCount = retryCount >= 0
            ? retryCount
            : throw new ArgumentOutOfRangeException(
                nameof(retryCount),
                "The retry count cannot be negative.");
        LeaseOwner = RequireIdentifier(leaseOwner, nameof(leaseOwner));
        ClaimedAt = RequireUtc(claimedAt, nameof(claimedAt));
        LeaseExpiresAt = RequireUtc(leaseExpiresAt, nameof(leaseExpiresAt));
        if (LeaseExpiresAt <= ClaimedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "The lease expiry must be later than the claim time.");
        }
    }

    public string OutboxId { get; }

    public string BranchId { get; }

    public long BranchSequence { get; }

    public string RequestId { get; }

    public TableSessionId TableSessionId { get; }

    public TableSessionAction Action { get; }

    public long TableVersion { get; }

    public DateTimeOffset CreatedAt { get; }

    public int RetryCount { get; }

    public string LeaseOwner { get; }

    public DateTimeOffset ClaimedAt { get; }

    public DateTimeOffset LeaseExpiresAt { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("The timestamp must be UTC.", parameterName);
}

public interface IBranchCloudOutboxClaimStore
{
    /// <summary>
    /// Claims the earliest unconfirmed event for one Branch. Implementations
    /// must not claim a later sequence while an earlier sequence remains
    /// unconfirmed, including when that earlier event is waiting for retry.
    /// </summary>
    Task<BranchCloudOutboxLease?> ClaimNextAsync(
        string branchId,
        string leaseOwner,
        DateTimeOffset claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}

public interface IBranchCloudOutboxLeaseRenewalStore
{
    /// <summary>
    /// Extends an active lease only when the persisted lease still matches the
    /// supplied owner and expiry token. Implementations must reject expired,
    /// replaced, confirmed, or non-extending renewal attempts.
    /// </summary>
    Task<BranchCloudOutboxLease> RenewAsync(
        BranchCloudOutboxLease lease,
        DateTimeOffset renewedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}
