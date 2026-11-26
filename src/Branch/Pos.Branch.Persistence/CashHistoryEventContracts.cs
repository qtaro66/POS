namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record CashHistoryEventQuery(string Source = "Session", int Limit = 20, string? Cursor = null);
public sealed record CashHistoryEventPage(int SchemaVersion, string BranchId, DateTimeOffset AsOf,
    string CashRequestId, string TableSessionId, string Source, IReadOnlyList<CashHistoryEvent> Events, string? NextCursor);
public sealed record CashHistoryEvent(string EventId, string Source, string RequestId, DateTimeOffset OccurredAt,
    string EmployeeId, string? DeviceId, CashHistorySessionEvent? Session, CashHistoryCancellationEvent? Cancellation);
public sealed record CashHistorySessionEvent(string Action, string? PreviousStatus, string CurrentStatus,
    string PreviousTableId, string CurrentTableId, long TableVersion, string? Reason);
public sealed record CashHistoryCancellationEvent(string Outcome, IReadOnlyList<CashHistoryCancellationLine> Lines);
public sealed record CashHistoryCancellationLine(string OrderLineId, CashHistoryProductionCounts Checked,
    CashHistoryProductionCounts Cancelled, int RequestedQuantity, long NotCancelledQuantity);
public sealed record CashHistoryProductionCounts(int NotStarted, int InProgress, int Completed, int NotApplicable);
