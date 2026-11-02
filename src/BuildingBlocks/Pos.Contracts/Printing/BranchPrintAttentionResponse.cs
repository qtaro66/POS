namespace Pos.Contracts.Printing;

public sealed record BranchPrintAttentionResponse(
    int SchemaVersion,
    DateTimeOffset ObservedAt,
    long TotalCount,
    IReadOnlyList<BranchPrintAttentionItem> Items,
    string? NextCursor);

public sealed record BranchPrintAttentionItem(
    string PrintJobId,
    string RequestId,
    string TableSessionId,
    string SnapshotId,
    string Purpose,
    string TargetStation,
    string Status,
    DateTimeOffset CreatedAt,
    string? AssignedPrinterId,
    DateTimeOffset? NextRetryAt,
    string AttentionReason,
    string? LatestAttemptStatus,
    DateTimeOffset? LatestAttemptFinishedAt);

public sealed record BranchPrintAttentionError(int SchemaVersion, string Code);
