namespace Pos.Contracts.Security;

public sealed record BranchPairingChallengeResponse(
    int SchemaVersion,
    string BranchServiceUrl,
    string BranchId,
    string PairingId,
    string OneTimeToken,
    DateTimeOffset ExpiresAt);
