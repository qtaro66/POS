namespace Pos.Contracts.Synchronization;

public sealed record BranchCloudEventSequenceGapResponse(
    int SchemaVersion,
    string ErrorCode,
    string BranchId,
    long ExpectedBranchSequence,
    long ReceivedBranchSequence)
{
    public const string ErrorCodeValue = "BranchSequenceGap";
}
