using Pos.Domain.StoreSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record ClosingReportShiftContext(string ShiftId, decimal? OpeningCashAmount,
    StoreOperationContext Opened, ShiftClosing Closing);
public sealed record ClosingReportLifecycleContext(int SchemaVersion, string BranchId, string RequestId,
    string StoreSessionId, string ClosingShiftId, DateOnly BusinessDate, decimal OpeningCashAmount,
    StoreOperationContext Opened, bool ClosesStore, IReadOnlyList<ClosingReportShiftContext> Shifts);
