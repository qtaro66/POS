using Pos.Domain.TableSessions;

namespace Pos.Application.Synchronization;

/// <summary>
/// Immutable event data that may cross the Branch-to-Cloud delivery boundary.
/// Lease ownership, retry counters, and other local processing state are
/// intentionally excluded.
/// </summary>
public sealed class BranchCloudOutboxEventEnvelope
{
    private BranchCloudOutboxEventEnvelope(
        string eventId,
        string branchId,
        long branchSequence,
        string requestId,
        TableSessionId tableSessionId,
        TableSessionAction action,
        long tableVersion,
        DateTimeOffset occurredAt)
    {
        EventId = RequireIdentifier(eventId, nameof(eventId));
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
        OccurredAt = occurredAt.Offset == TimeSpan.Zero
            ? occurredAt
            : throw new ArgumentException(
                "The event timestamp must be UTC.",
                nameof(occurredAt));
    }

    public string EventId { get; }

    public string BranchId { get; }

    public long BranchSequence { get; }

    public string RequestId { get; }

    public TableSessionId TableSessionId { get; }

    public TableSessionAction Action { get; }

    public long TableVersion { get; }

    public DateTimeOffset OccurredAt { get; }

    public static BranchCloudOutboxEventEnvelope FromLease(
        BranchCloudOutboxLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        return Create(
            lease.OutboxId,
            lease.BranchId,
            lease.BranchSequence,
            lease.RequestId,
            lease.TableSessionId,
            lease.Action,
            lease.TableVersion,
            lease.CreatedAt);
    }

    public static BranchCloudOutboxEventEnvelope FromReplayLease(
        BranchCloudOutboxSequenceReplayLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return Create(
            lease.OutboxId,
            lease.BranchId,
            lease.BranchSequence,
            lease.RequestId,
            lease.TableSessionId,
            lease.Action,
            lease.TableVersion,
            lease.CreatedAt);
    }

    public static BranchCloudOutboxEventEnvelope Create(
        string eventId,
        string branchId,
        long branchSequence,
        string requestId,
        TableSessionId tableSessionId,
        TableSessionAction action,
        long tableVersion,
        DateTimeOffset occurredAt) =>
        new(
            eventId,
            branchId,
            branchSequence,
            requestId,
            tableSessionId,
            action,
            tableVersion,
            occurredAt);

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
