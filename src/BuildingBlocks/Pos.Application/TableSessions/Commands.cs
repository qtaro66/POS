using Pos.Domain.TableSessions;

namespace Pos.Application.TableSessions;

public sealed record OpenTableCommand(
    BranchCommandMetadata Metadata,
    TableId TableId);

public sealed record RequestPaymentCommand(
    BranchCommandMetadata Metadata,
    long ExpectedVersion);
