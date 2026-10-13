using Pos.Domain.TableSessions;

namespace Pos.Application.TableSessions;

public sealed record TableSessionAuditRecord(
    string AuditId,
    string BranchId,
    string RequestId,
    TableSessionTransitioned Transition);

public sealed record CloudSyncOutboxRecord(
    string OutboxId,
    string BranchId,
    string RequestId,
    TableSessionId TableSessionId,
    TableSessionAction Action,
    long TableVersion,
    DateTimeOffset CreatedAt);
