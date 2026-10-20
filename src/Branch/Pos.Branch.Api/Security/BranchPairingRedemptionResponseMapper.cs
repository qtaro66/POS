using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchPairingRedemptionResponseMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchPairingRedemptionResponse Map(RedeemedBranchDevice redeemed)
    {
        ArgumentNullException.ThrowIfNull(redeemed);
        if (!BranchDeviceIdentifierFormat.IsCanonical(redeemed.DeviceId))
        {
            throw new ArgumentException(
                "The device identifier is not canonical.",
                nameof(redeemed));
        }

        if (!BranchDeviceCredentialFormat.TryParse(
                redeemed.DeviceCredential,
                out var parsedCredential))
        {
            throw new ArgumentException(
                "The device credential is not canonical.",
                nameof(redeemed));
        }

        parsedCredential.Dispose();
        return new BranchPairingRedemptionResponse(
            CurrentSchemaVersion,
            redeemed.DeviceId,
            redeemed.DeviceCredential);
    }
}
