namespace Pos.Application.TableSessions;

public sealed record BranchCancellationQueueLine(string OrderLineId, string DisplayName, int RequestedQuantity, CancellationProductionCheck? Checked = null);

public sealed record BranchCancellationQueueEntry(
    string CancellationRequestId, string TableSessionId, string Status, IReadOnlyList<BranchCancellationQueueLine> Lines);
