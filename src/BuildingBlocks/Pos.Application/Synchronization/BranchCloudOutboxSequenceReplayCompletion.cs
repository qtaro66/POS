namespace Pos.Application.Synchronization;

public sealed class BranchCloudOutboxSequenceReplayCompletion
{
    public BranchCloudOutboxSequenceReplayCompletion(
        BranchCloudOutboxSequenceReplayLease lease,
        string acknowledgedEventId,
        long acknowledgedBranchSequence,
        DateTimeOffset acknowledgedAt)
    {
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        if (!string.Equals(acknowledgedEventId, lease.OutboxId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The acknowledgment event must match the replay lease.",
                nameof(acknowledgedEventId));
        }
        if (acknowledgedBranchSequence != lease.BranchSequence)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acknowledgedBranchSequence),
                "The acknowledgment sequence must match the replay lease.");
        }
        if (acknowledgedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The acknowledgment timestamp must be UTC.", nameof(acknowledgedAt));
        }
        if (acknowledgedAt < lease.ClaimedAt || acknowledgedAt >= lease.LeaseExpiresAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acknowledgedAt),
                "The acknowledgment time must fall within the active replay lease window.");
        }
        AcknowledgedAt = acknowledgedAt;
    }

    public BranchCloudOutboxSequenceReplayLease Lease { get; }

    public DateTimeOffset AcknowledgedAt { get; }
}

public enum BranchCloudOutboxSequenceReplayCompletionResult
{
    Completed,
    AlreadyCompleted,
    NotFound
}

public interface IBranchCloudOutboxSequenceReplayCompletionStore
{
    /// <summary>
    /// Atomically completes a replay request and releases its lease after a
    /// matching durable Cloud acknowledgment. Requires exact persisted recovery
    /// and event identity and active ownership. Repeated completion of the same
    /// recovery and event returns AlreadyCompleted and preserves first completion
    /// time. Missing or cross-Branch recovery returns NotFound. Stale or replaced
    /// ownership fails closed. Original event acknowledgment remains unchanged.
    /// </summary>
    Task<BranchCloudOutboxSequenceReplayCompletionResult> CompleteAsync(
        BranchCloudOutboxSequenceReplayCompletion completion,
        CancellationToken cancellationToken = default);
}
