using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Ordering;

public static class BranchEmployeePermissionRefreshHttpHandler
{
    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal, HttpContext context,
        BranchEmployeePermissionCache cache, BranchEmployeePermissionPollingService polling, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (!cache.IsForBranch(identity.BranchId)
            || !AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role)
            || role != "PcPos") { return TypedResults.StatusCode(403); }
        if (context.Request.ContentLength > 0 || context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return TypedResults.StatusCode(413);
        }

        var result = await polling.RefreshAsync(cancellationToken);
        return TypedResults.Ok(new { schemaVersion = 1, outcome = result.ToString() });
    }
}
