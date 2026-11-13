namespace Pos.Application.TableSessions;

public sealed record BranchFloorPlan(
    string MasterDataVersion,
    IReadOnlyList<BranchFloorPlanTable> Tables);

public sealed record BranchFloorPlanTable(
    string TableId,
    string DisplayName,
    string? Zone,
    string Status,
    string? TableSessionId,
    long? TableVersion);
