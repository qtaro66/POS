namespace Pos.Contracts.Synchronization;

public sealed record BranchCloudEventIngestionRequest(
    int SchemaVersion,
    string EventId,
    string BranchId,
    long BranchSequence,
    string RequestId,
    string TableSessionId,
    string Action,
    long TableVersion,
    DateTimeOffset OccurredAt);
