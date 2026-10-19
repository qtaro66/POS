using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchPairingChallengeResponseMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchPairingChallengeResponse Map(
        string branchServiceUrl,
        string branchId,
        IssuedBranchPairingChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var serviceUri = BranchPairingPhaseZeroServiceOrigin.Require(
            branchServiceUrl,
            nameof(branchServiceUrl));
        branchId = RequireIdentifier(branchId, nameof(branchId));
        if (!BranchPairingTokenFormat.IsCanonicalPairingId(challenge.PairingId))
        {
            throw new ArgumentException(
                "The pairing identifier is not canonical.",
                nameof(challenge));
        }

        if (!BranchPairingTokenFormat.TryParse(challenge.Token, out var parsed))
        {
            throw new ArgumentException(
                "The one-time pairing token is not canonical.",
                nameof(challenge));
        }

        parsed.Dispose();

        if (challenge.ExpiresAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The pairing expiry must be UTC.",
                nameof(challenge));
        }

        return new BranchPairingChallengeResponse(
            CurrentSchemaVersion,
            serviceUri.AbsoluteUri,
            branchId,
            challenge.PairingId,
            challenge.Token,
            challenge.ExpiresAt);
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
