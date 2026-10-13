using Pos.Domain.TableSessions;

namespace Pos.Application.TableSessions;

public enum BranchCommandOutcome
{
    BranchAccepted,
    Rejected
}

public sealed record BranchCommandReceipt(
    string RequestId,
    string BranchId,
    string DeviceId,
    string EmployeeId,
    DateTimeOffset ClientCreatedAt,
    BranchCommandOutcome Outcome,
    TableSessionId TableSessionId,
    TableSessionStatus? SessionStatus,
    long? TableVersion,
    string? ErrorCode,
    DateTimeOffset ServerReceivedAt);
