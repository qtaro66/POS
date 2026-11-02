namespace Pos.Contracts.Synchronization;

public sealed record BranchCloudOutboxStatusResponse(
    int SchemaVersion, DateTimeOffset ObservedAt, BranchCloudOutboxSummaryResponse Summary,
    IReadOnlyList<BranchCloudOutboxStatusItemResponse> Items, string? NextAfterSequence);

public sealed record BranchCloudOutboxSummaryResponse(long UnacknowledgedCount, long PendingCount, long ProcessingCount, long FailedCount, long OtherCount);

public sealed record BranchCloudOutboxStatusItemResponse(
    string OutboxId, string BranchSequence, string RequestId, string TableSessionId, string Action,
    string ProcessingStatus, DateTimeOffset CreatedAt, int RetryCount, DateTimeOffset? NextRetryAt,
    DateTimeOffset? LeaseExpiresAt, DateTimeOffset? LastFailedAt, string? ExpectedBranchSequence);

public sealed record BranchCloudOutboxStatusError(int SchemaVersion, string Code);
