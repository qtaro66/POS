namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record CashHistoryShiftContext(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    string CashRequestId, string TableSessionId, string StoreSessionId, string ShiftId, string BusinessDate,
    IReadOnlyList<CashHistoryShiftEntry> Entries);
public sealed record CashHistoryShiftEntry(string RequestId, string Action, DateTimeOffset OccurredAt,
    string BranchLocalDate, string EmployeeId, string DeviceId, decimal? CashAmount, CashHistoryShiftClosing? Closing);
public sealed record CashHistoryShiftClosing(bool ClosesStore, decimal ExpectedCashAmount,
    decimal? ActualCashAmount, decimal? CashDifference);
