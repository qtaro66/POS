namespace Pos.Contracts.Security;

public sealed record BranchPairingRedemptionRequest(
    int SchemaVersion,
    string BranchId,
    string PairingId,
    string OneTimeToken,
    string DeviceDisplayName);

public sealed record BranchPairingRedemptionResponse(
    int SchemaVersion,
    string DeviceId,
    string DeviceCredential);
