namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record CashHistoryProductionQuery(string? OrderBatchId = null, string? OrderLineId = null,
    string? ProductionStationId = null, int Limit = 20, string? Cursor = null);
public sealed record CashHistoryProductionItem(string SnapshotId, string OrderBatchId, string SubmittedRequestId,
    string TableSessionId, string TableId, string ProductionStationId, DateTimeOffset SubmittedAt,
    int LineCount, IReadOnlyList<string> OrderLineIds);
public sealed record CashHistoryProductionPage(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    string CashRequestId, string TableSessionId, IReadOnlyList<CashHistoryProductionItem> Items, string? NextCursor);
public sealed record CashHistoryProductionDetail(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    string CashRequestId, string TableSessionId, CashHistoryProductionItem Snapshot, IReadOnlyList<CashHistoryProductionLine> Lines);
public sealed record CashHistoryProductionLine(string OrderLineId, int LineSequence, string DisplayName,
    int Quantity, string FulfillmentType, string? Comment, IReadOnlyList<CashHistoryProductionOption> Options);
public sealed record CashHistoryProductionOption(string? OptionGroupId, string DisplayName, int QuantityPerItem);
