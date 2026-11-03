using System.Globalization;
using System.Security.Claims;
using Pos.Contracts.Security;

namespace Pos.Central.Api.Synchronization;

public static class CentralBranchEmployeePermissions
{
    public const string Policy = "ReadBranchEmployeePermissions";
    public const string Route = "/api/v1/branch-employee-permissions";
    public const string Source = "CentralEmployeePermissions:Branches";

    public static IResult Read(HttpContext context, ClaimsPrincipal principal, IConfiguration configuration)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.IsHttps || !AuthenticatedCentralBranchIdentityResolver.TryResolve(principal, out var identity))
        {
            return TypedResults.Unauthorized();
        }

        var entries = configuration.GetSection(Source).GetChildren()
            .Where(entry => string.Equals(entry["BranchId"], identity.BranchId, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (entries.Length != 1 || !long.TryParse(entries[0]["Revision"], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 1)
        {
            return TypedResults.StatusCode(503);
        }

        var employees = new List<BranchEmployeePermissionEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var employee in entries[0].GetSection("Employees").GetChildren())
        {
            if (employees.Count == 10000 || employee["EmployeeId"] is not { Length: > 0 and <= 128 } id
                || id != id.Trim() || id.Any(char.IsControl) || !ids.Add(id)
                || !bool.TryParse(employee["Active"], out var active)
                || !bool.TryParse(employee["SetSoldOut"], out var setSoldOut))
            {
                return TypedResults.StatusCode(503);
            }

            employees.Add(new(id, active, active && setSoldOut));
        }

        return TypedResults.Ok(new BranchEmployeePermissionSnapshot(1, identity.BranchId, revision, employees));
    }
}
