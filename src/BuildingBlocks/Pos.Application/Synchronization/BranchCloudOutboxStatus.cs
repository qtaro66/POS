namespace Pos.Application.Synchronization;

public sealed record BranchCloudOutboxSummary(long UnacknowledgedCount, long PendingCount, long ProcessingCount, long FailedCount, long OtherCount);

public sealed record BranchCloudOutboxStatusItem(
    string OutboxId, long BranchSequence, string RequestId, string TableSessionId, string Action,
    string ProcessingStatus, DateTimeOffset CreatedAt, int RetryCount, DateTimeOffset? NextRetryAt,
    DateTimeOffset? LeaseExpiresAt, DateTimeOffset? LastFailedAt, long? ExpectedBranchSequence);

public sealed record BranchCloudOutboxStatusPage(
    BranchCloudOutboxSummary Summary,
    IReadOnlyList<BranchCloudOutboxStatusItem> Items,
    long? NextAfterSequence);

public interface IBranchCloudOutboxStatusQuery
{
    Task<BranchCloudOutboxStatusPage> ListAsync(string branchId, int maximumResults, long? afterSequence = null, CancellationToken cancellationToken = default);
}
