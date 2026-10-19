namespace Pos.Branch.Api.Security;

internal static class BranchPairingPhaseZeroServiceOrigin
{
    public static Uri Require(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" ||
            uri.Port == 0 ||
            uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            throw new ArgumentException(
                "The Branch service URL must be an absolute HTTPS IP origin without credentials, path, query, or fragment.",
                parameterName);
        }

        return uri;
    }
}
