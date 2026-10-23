namespace Pos.Contracts.Security;

public sealed record BranchEmployeeDataSyncStatusResponse(
    int SchemaVersion,
    DateTimeOffset? LastSynchronizedAt);
