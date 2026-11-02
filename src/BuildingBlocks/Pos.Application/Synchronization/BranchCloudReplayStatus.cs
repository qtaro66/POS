namespace Pos.Application.Synchronization;

public sealed record BranchCloudReplaySummary(long PendingCount, long LeasedCount, long UnleasedCount, long WithRetriesCount);

public sealed record BranchCloudReplayStatusItem(
    string RecoveryId, string OutboxId, long ExpectedBranchSequence, long ReceivedBranchSequence,
    string RequestId, string TableSessionId, string Action, DateTimeOffset RequestedAt,
    bool HasLease, int RetryCount, DateTimeOffset? NextRetryAt, DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastFailedAt, long? LastExpectedBranchSequence);

public sealed record BranchCloudReplayStatusPage(
    BranchCloudReplaySummary Summary, IReadOnlyList<BranchCloudReplayStatusItem> Items, long? NextAfterSequence);

public interface IBranchCloudReplayStatusQuery
{
    Task<BranchCloudReplayStatusPage> ListAsync(string branchId, int maximumResults, long? afterSequence = null, CancellationToken cancellationToken = default);
}
