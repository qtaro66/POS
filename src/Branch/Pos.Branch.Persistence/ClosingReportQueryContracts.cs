using Pos.Domain.StoreSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record ClosingReportQuery(string Status = "All", string? StoreSessionId = null,
    string? ShiftId = null, string? BusinessDate = null, int Limit = 20, string? Cursor = null,
    string LifecycleStatus = "All");
public sealed record ClosingReportListItem(string RequestId, string ReportId, string StoreSessionId, string ShiftId,
    string BusinessDate, string Operation, DateTimeOffset CreatedAt, string DeliveryState, int AttemptCount,
    DateTimeOffset AvailableAt, DateTimeOffset? LeaseExpiresAt, DateTimeOffset? DeliveredAt, string? LastFailure);
public sealed record ClosingReportPage(string BranchId, DateTimeOffset AsOf,
    IReadOnlyList<ClosingReportListItem> Items, string? NextCursor);
public sealed record ClosingReportSnapshot(int SchemaVersion, string ReportId, string BranchId, string RequestId,
    string StoreSessionId, string ShiftId, string Operation, DateOnly BusinessDate, ShiftClosing Closing,
    StoreCashSummary? CashSummary);
public sealed record ClosingReportDetail(string BranchId, ClosingReportListItem Report, ClosingReportSnapshot Snapshot,
    ClosingReportLifecycleContext? Lifecycle = null, ClosingLifecycleDelivery? LifecycleDelivery = null);
