namespace Pos.Branch.Api.Synchronization;

internal static class BranchCloudServiceOrigin
{
    public static Uri Require(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" ||
            uri.Port == 0)
        {
            throw new ArgumentException(
                "ServiceOrigin must be an absolute HTTPS origin without credentials, path, query, or fragment.",
                parameterName);
        }

        return uri;
    }
}
