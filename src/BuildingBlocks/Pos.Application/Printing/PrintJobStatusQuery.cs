using Pos.Domain.TableSessions;

namespace Pos.Application.Printing;

public enum PersistentPrintAttemptStatus
{
    Connecting,
    Dispatching,
    PrintDispatched,
    Failed,
    Unknown
}

public sealed record PrintAttemptStatusSnapshot(
    string PrintAttemptId,
    string PrinterId,
    int AttemptNumber,
    string PayloadSha256,
    PersistentPrintAttemptStatus Status,
    bool AutomaticRetryAllowed,
    string? ErrorCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FirstByteAt,
    DateTimeOffset? FinishedAt);

public sealed record PrintJobStatusSnapshot(
    string PrintJobId,
    string BranchId,
    string RequestId,
    TableSessionId TableSessionId,
    string SnapshotId,
    PrintJobPurpose Purpose,
    PrintTargetStation TargetStation,
    PersistentPrintJobStatus Status,
    int AttemptCount,
    DateTimeOffset? NextRetryAt,
    string? LastError,
    string? BlockedReason,
    DateTimeOffset CreatedAt,
    string? AssignedPrinterId,
    DateTimeOffset? LeaseExpiresAt,
    string? OriginalPrintJobId,
    bool IsReprint,
    IReadOnlyList<PrintAttemptStatusSnapshot> Attempts);

public interface IPrintJobStatusQuery
{
    Task<PrintJobStatusSnapshot?> FindAsync(
        string branchId,
        string printJobId,
        CancellationToken cancellationToken = default);
}
