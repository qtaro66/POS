namespace Pos.Branch.Api.Synchronization;

internal static class BranchCloudDeliveryEndpoint
{
    public static Uri Require(
        string serviceOrigin,
        string deliveryPath,
        string originParameterName,
        string pathParameterName)
    {
        var origin = BranchCloudServiceOrigin.Require(
            serviceOrigin,
            originParameterName);

        if (!IsCanonicalPath(deliveryPath))
        {
            throw new ArgumentException(
                "DeliveryPath must be a canonical absolute URL path containing only unreserved characters and path separators, without traversal, encoding, query, or fragment.",
                pathParameterName);
        }

        return new Uri(origin, deliveryPath);
    }

    private static bool IsCanonicalPath(string value)
    {
        if (string.IsNullOrEmpty(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = value.Split('/');
        return segments.Length >= 2 &&
            segments.Skip(1).All(IsCanonicalSegment);
    }

    private static bool IsCanonicalSegment(string segment) =>
        segment.Length > 0 &&
        segment is not ("." or "..") &&
        segment.All(character =>
            character is >= 'A' and <= 'Z' or
                >= 'a' and <= 'z' or
                >= '0' and <= '9' or
                '-' or '.' or '_' or '~');
}
