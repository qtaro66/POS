using Pos.Domain.TableSessions;

namespace Pos.Application.Printing;

public sealed record PrintJobAttentionCursor(
    DateTimeOffset CreatedAt,
    string PrintJobId);

public sealed record PrintJobAttentionItem(
    string PrintJobId,
    string RequestId,
    TableSessionId TableSessionId,
    string SnapshotId,
    PrintJobPurpose Purpose,
    PrintTargetStation TargetStation,
    PersistentPrintJobStatus Status,
    DateTimeOffset CreatedAt,
    string? AssignedPrinterId,
    DateTimeOffset? NextRetryAt,
    string? BlockedReason,
    string? LastError,
    PersistentPrintAttemptStatus? LatestAttemptStatus,
    string? LatestErrorCode,
    DateTimeOffset? LatestAttemptFinishedAt);

public sealed record PrintJobAttentionPage(
    long TotalCount,
    IReadOnlyList<PrintJobAttentionItem> Items,
    PrintJobAttentionCursor? NextCursor);

public interface IPrintJobAttentionQuery
{
    Task<PrintJobAttentionPage> ListAsync(
        string branchId,
        DateTimeOffset observedAt,
        int maximumResults,
        PrintJobAttentionCursor? cursor = null,
        CancellationToken cancellationToken = default);
}
