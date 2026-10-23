namespace Pos.Contracts.Synchronization;

public sealed record BranchCloudDataSentStatusResponse(
    int SchemaVersion,
    DateTimeOffset? LastSentAt);
