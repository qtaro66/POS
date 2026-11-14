namespace Pos.Application.TableSessions;

public sealed record CancellationProductionCheck(
    string OrderLineId, int NotStarted, int InProgress, int Completed, int NotApplicable);
