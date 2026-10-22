namespace Pos.Branch.Api.Security;

internal static class BranchSecretResponseHeaders
{
    public static void Apply(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }
}
