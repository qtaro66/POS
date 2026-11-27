namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record CashHistoryPrintQuery(int Limit = 20, string? Cursor = null);
public sealed record CashHistoryPrintJob(string PrintJobId, string RequestId, string SnapshotId,
    string ProductionStationId, string Status, int AttemptCount, DateTimeOffset CreatedAt,
    bool IsReprint, string? OriginalPrintJobId);
public sealed record CashHistoryPrintEvidence(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    string CashRequestId, string TableSessionId, string SnapshotId, CashHistoryPrintJob? Job);
public sealed record CashHistoryPrintAttempt(string PrintAttemptId, int AttemptNumber, string Status,
    bool AutomaticRetryAllowed, DateTimeOffset StartedAt, DateTimeOffset? FirstByteAt, DateTimeOffset? FinishedAt);
public sealed record CashHistoryPrintAttemptsPage(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    string CashRequestId, string TableSessionId, string SnapshotId, CashHistoryPrintJob Job,
    IReadOnlyList<CashHistoryPrintAttempt> Items, string? NextCursor);
public sealed class CashHistoryPrintJobNotFoundException : KeyNotFoundException
{
    public CashHistoryPrintJobNotFoundException() : base("Recorded print job not found.") { }
}
