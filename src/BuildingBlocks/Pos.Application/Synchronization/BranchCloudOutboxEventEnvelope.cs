using Pos.Domain.TableSessions;

namespace Pos.Application.Synchronization;

/// <summary>
/// Immutable event data that may cross the Branch-to-Cloud delivery boundary.
/// Lease ownership, retry counters, and other local processing state are
/// intentionally excluded.
/// </summary>
public sealed class BranchCloudOutboxEventEnvelope
{
    private BranchCloudOutboxEventEnvelope(BranchCloudOutboxLease lease)
    {
        EventId = lease.OutboxId;
        BranchId = lease.BranchId;
        BranchSequence = lease.BranchSequence;
        RequestId = lease.RequestId;
        TableSessionId = lease.TableSessionId;
        Action = lease.Action;
        TableVersion = lease.TableVersion;
        OccurredAt = lease.CreatedAt;
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

        return new BranchCloudOutboxEventEnvelope(lease);
    }
}
