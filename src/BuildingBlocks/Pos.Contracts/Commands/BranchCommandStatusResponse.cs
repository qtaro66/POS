namespace Pos.Contracts.Commands;

public static class BranchCommandStatusValues
{
    public const string BranchAccepted = nameof(BranchAccepted);
    public const string Rejected = nameof(Rejected);
    public const string NoDurableResult = nameof(NoDurableResult);
}

public sealed record BranchCommandStatusResponse(
    int SchemaVersion,
    string RequestId,
    string Status,
    string? TableSessionId,
    string? TableSessionStatus,
    long? TableVersion,
    string? ErrorCode,
    DateTimeOffset? ServerReceivedAt,
    long? BranchSequence);
