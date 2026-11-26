namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record CashHistoryQuery(string? BusinessDate = null, string? ReceiptNumber = null,
    string? TableSessionId = null, string PaymentMethod = "All", string Status = "All", int Limit = 20, string? Cursor = null,
    string? TableId = null, string? OrderBatchId = null);
public sealed record CashHistoryItem(string RequestId, string TableSessionId, string ReceiptNumber, string BusinessDate,
    string StoreSessionId, string ShiftId, string EmployeeId, string DeviceId, DateTimeOffset CapturedAt,
    string PaymentMethod, string Status, decimal TenderedAmount, decimal AppliedAmount, decimal ChangeAmount,
    string CapturedPrintStatus, string? TableId = null, IReadOnlyList<string>? OrderBatchIds = null);
public sealed record CashHistoryPage(int SchemaVersion, string BranchId, DateTimeOffset AsOf, string BusinessDate,
    IReadOnlyList<CashHistoryItem> Items, string? NextCursor);
public sealed record CashHistoryDetail(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    CashHistoryItem Transaction, StoreBillTotals? Financials, CashHistoryBill? Bill = null);
