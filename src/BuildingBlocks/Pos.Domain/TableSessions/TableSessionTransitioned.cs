namespace Pos.Domain.TableSessions;

public sealed record TableSessionTransitioned(
    TableSessionId TableSessionId,
    TableSessionAction Action,
    TableSessionStatus? PreviousStatus,
    TableSessionStatus CurrentStatus,
    TableId PreviousTableId,
    TableId CurrentTableId,
    long Version,
    string EmployeeId,
    string DeviceId,
    DateTimeOffset OccurredAt,
    string? Reason);
