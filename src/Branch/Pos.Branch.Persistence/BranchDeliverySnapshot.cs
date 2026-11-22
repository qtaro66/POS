namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record DeliveryChannelSummary(long TotalCount, long PendingCount, long DeliveredCount,
    long InFlightCount, long DelayedCount, long ExpiredLeaseCount, long UnknownCount,
    DateTimeOffset? LastConfirmedAt, DateTimeOffset? OldestPendingAt);
public sealed record BranchDeliverySnapshot(string BranchId, DateTimeOffset AsOf, string State,
    DateTimeOffset? LastConfirmedAt, DeliveryChannelSummary TableEvents, DeliveryChannelSummary ClosingReports,
    long ReplayPendingCount, long ReplayCompletedCount, DateTimeOffset? ReplayLastConfirmedAt,
    DateTimeOffset? ReplayOldestPendingAt, DeliveryChannelSummary? LifecycleReports = null);
