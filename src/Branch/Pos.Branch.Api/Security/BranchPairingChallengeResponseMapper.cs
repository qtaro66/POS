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

        var serviceUri = RequirePhaseZeroServiceUri(branchServiceUrl);
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

    private static Uri RequirePhaseZeroServiceUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" ||
            uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            throw new ArgumentException(
                "The Branch service URL must be an absolute HTTPS IP origin without credentials, path, query, or fragment.",
                nameof(value));
        }

        return uri;
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
