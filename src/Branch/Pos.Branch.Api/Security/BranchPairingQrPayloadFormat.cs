using System.Diagnostics.CodeAnalysis;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public sealed record ParsedBranchPairingQrPayload(
    string BranchServiceUrl,
    string BranchId,
    string PairingId,
    string OneTimeToken);

public static class BranchPairingQrPayloadFormat
{
    public const int CurrentVersion = 1;

    private static readonly string FragmentPrefix =
        $"pair?v={CurrentVersion}&branchId=";
    private const string PairingIdSeparator = "&pairingId=";
    private const string TokenSeparator = "&token=";

    public static string Create(BranchPairingChallengeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.SchemaVersion != BranchPairingChallengeResponseMapper.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                "The pairing challenge response schema is not supported.",
                nameof(response));
        }

        var serviceUri = BranchPairingPhaseZeroServiceOrigin.Require(
            response.BranchServiceUrl,
            nameof(response));
        var branchId = RequireCanonicalBranchId(response.BranchId, nameof(response));
        ValidatePairingIdentity(response.PairingId, response.OneTimeToken, nameof(response));

        return BuildCanonicalPayload(
            serviceUri,
            branchId,
            response.PairingId,
            response.OneTimeToken);
    }

    public static bool TryParse(
        string? payload,
        [NotNullWhen(true)] out ParsedBranchPairingQrPayload? parsed)
    {
        parsed = null;
        if (string.IsNullOrEmpty(payload) ||
            !string.Equals(payload, payload.Trim(), StringComparison.Ordinal) ||
            !Uri.TryCreate(payload, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.Query) ||
            uri.AbsolutePath != "/")
        {
            return false;
        }

        Uri serviceUri;
        try
        {
            serviceUri = BranchPairingPhaseZeroServiceOrigin.Require(
                uri.GetLeftPart(UriPartial.Authority),
                nameof(payload));
        }
        catch (ArgumentException)
        {
            return false;
        }

        var fragment = uri.GetComponents(UriComponents.Fragment, UriFormat.UriEscaped);
        if (!fragment.StartsWith(FragmentPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var branchStart = FragmentPrefix.Length;
        var pairingSeparator = fragment.IndexOf(
            PairingIdSeparator,
            branchStart,
            StringComparison.Ordinal);
        if (pairingSeparator <= branchStart)
        {
            return false;
        }

        var pairingStart = pairingSeparator + PairingIdSeparator.Length;
        var tokenSeparator = fragment.IndexOf(
            TokenSeparator,
            pairingStart,
            StringComparison.Ordinal);
        if (tokenSeparator <= pairingStart ||
            fragment.IndexOf('&', tokenSeparator + TokenSeparator.Length) >= 0)
        {
            return false;
        }

        var encodedBranchId = fragment[branchStart..pairingSeparator];
        var pairingId = fragment[pairingStart..tokenSeparator];
        var token = fragment[(tokenSeparator + TokenSeparator.Length)..];

        string branchId;
        try
        {
            branchId = Uri.UnescapeDataString(encodedBranchId);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(branchId) ||
            !string.Equals(branchId, branchId.Trim(), StringComparison.Ordinal) ||
            !string.Equals(
                Uri.EscapeDataString(branchId),
                encodedBranchId,
                StringComparison.Ordinal) ||
            !TryValidatePairingIdentity(pairingId, token))
        {
            return false;
        }

        var canonicalPayload = BuildCanonicalPayload(
            serviceUri,
            branchId,
            pairingId,
            token);
        if (!string.Equals(canonicalPayload, payload, StringComparison.Ordinal))
        {
            return false;
        }

        parsed = new ParsedBranchPairingQrPayload(
            serviceUri.AbsoluteUri,
            branchId,
            pairingId,
            token);
        return true;
    }

    private static string BuildCanonicalPayload(
        Uri serviceUri,
        string branchId,
        string pairingId,
        string token) =>
        string.Concat(
            serviceUri.AbsoluteUri,
            '#',
            FragmentPrefix,
            Uri.EscapeDataString(branchId),
            PairingIdSeparator,
            pairingId,
            TokenSeparator,
            token);

    private static string RequireCanonicalBranchId(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            ? throw new ArgumentException(
                "The Branch identifier must be canonical and non-empty.",
                parameterName)
            : value;

    private static void ValidatePairingIdentity(
        string pairingId,
        string token,
        string parameterName)
    {
        if (!TryValidatePairingIdentity(pairingId, token))
        {
            throw new ArgumentException(
                "The pairing identifier or one-time token is not canonical.",
                parameterName);
        }
    }

    private static bool TryValidatePairingIdentity(string pairingId, string token)
    {
        if (!BranchPairingTokenFormat.IsCanonicalPairingId(pairingId) ||
            !BranchPairingTokenFormat.TryParse(token, out var parsedToken))
        {
            return false;
        }

        parsedToken.Dispose();
        return true;
    }
}
