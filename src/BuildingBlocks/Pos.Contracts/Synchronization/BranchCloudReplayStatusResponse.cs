namespace Pos.Contracts.Synchronization;

public sealed record BranchCloudReplayStatusResponse(
    int SchemaVersion, DateTimeOffset ObservedAt, BranchCloudReplaySummaryResponse Summary,
    IReadOnlyList<BranchCloudReplayStatusItemResponse> Items, string? NextAfterSequence);
public sealed record BranchCloudReplaySummaryResponse(long PendingCount, long LeasedCount, long UnleasedCount, long WithRetriesCount);
public sealed record BranchCloudReplayStatusItemResponse(
    string RecoveryId, string OutboxId, string ExpectedBranchSequence, string ReceivedBranchSequence,
    string RequestId, string TableSessionId, string Action, DateTimeOffset RequestedAt,
    bool HasLease, int RetryCount, DateTimeOffset? NextRetryAt, DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastFailedAt, string? LastExpectedBranchSequence);
public sealed record BranchCloudReplayStatusError(int SchemaVersion, string Code);
