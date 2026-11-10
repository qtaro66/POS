using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Ordering;

public static class BranchSoldOutAuthorization
{
    public static bool CanAdjust(ClaimsPrincipal principal, AuthenticatedBranchRequestIdentity identity,
        BranchEmployeePermissionCache? permissions)
    {
        return AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role)
            && (role == "PcPos" || (role == "Mobile" && permissions?.CanSetSoldOut(identity.BranchId, identity.EmployeeId) == true));
    }

    public static IResult Read(ClaimsPrincipal principal, [FromServices] BranchEmployeePermissionCache? permissions = null)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        return TypedResults.Ok(new { schemaVersion = 1, canSetSoldOut = CanAdjust(principal, identity, permissions) });
    }
}
