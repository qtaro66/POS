namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record KitchenReprintCommand(string RequestId, string CashRequestId, string SnapshotId,
    string OrderLineId, string OriginalPrintJobId, string DeviceId, string RequesterEmployeeId,
    string ApproverEmployeeId, string Reason, DateTimeOffset QueuedAt);
public sealed record KitchenReprintReceipt(int SchemaVersion, string BranchId, string RequestId,
    string CashRequestId, string TableSessionId, string OrderBatchId, string OrderLineId,
    string OriginalSnapshotId, string OriginalPrintJobId, string ReprintSnapshotId,
    string ReprintPrintJobId, string ProductionStationId, DateTimeOffset QueuedAt);
public sealed record KitchenReprintStatus(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    KitchenReprintReceipt Receipt, string Status, int AttemptCount);
public sealed class KitchenReprintLineNotFoundException : KeyNotFoundException { }
public sealed class KitchenReprintConflictException : InvalidOperationException { }
