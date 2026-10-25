namespace Pos.Contracts.Synchronization;

public sealed record BranchCloudEventIngestionResponse(
    int SchemaVersion,
    string EventId,
    string BranchId,
    long BranchSequence);
