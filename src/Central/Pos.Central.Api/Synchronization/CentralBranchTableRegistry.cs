using System.Security.Claims;
using Pos.Contracts.Ordering;

namespace Pos.Central.Api.Synchronization;

public static class CentralBranchTableRegistry
{
    public const string Route = "/api/v1/branch-table-registry";

    public static IResult Read(HttpContext context, ClaimsPrincipal principal, IConfiguration configuration)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.IsHttps || !AuthenticatedCentralBranchIdentityResolver.TryResolve(principal, out var identity))
        {
            return TypedResults.Unauthorized();
        }

        var publications = configuration.GetSection("CentralTableRegistry:Publications").GetChildren()
            .Where(entry => entry["BranchId"] == identity.BranchId).Take(2).ToArray();
        if (publications.Length != 1 || publications[0]["Payload"] is not { } payload ||
            publications[0]["Sha256"] is not { } checksum)
        {
            return TypedResults.StatusCode(503);
        }

        var publication = new PublishedTableRegistry(payload, checksum);
        try
        {
            _ = TableRegistry.Read(publication, identity.BranchId);
            return TypedResults.Ok(publication);
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or System.Text.Json.JsonException)
        {
            return TypedResults.StatusCode(503);
        }
    }
}
