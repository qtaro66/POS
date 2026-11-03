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
        var claims = principal.Identities
            .Where(candidate => candidate.IsAuthenticated && candidate.AuthenticationType == BranchDeviceAuthenticationDefaults.SchemeName)
            .SelectMany(candidate => candidate.Claims);
        return AuthenticatedBranchClaimResolver.TryGetSingle(claims, BranchPosClaimTypes.DeviceRole, out var role)
            && (role == "PcPos" || (role == "Mobile" && permissions?.CanSetSoldOut(identity.BranchId, identity.EmployeeId) == true));
    }

    public static IResult Read(ClaimsPrincipal principal, [FromServices] BranchEmployeePermissionCache? permissions = null)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        return TypedResults.Ok(new { schemaVersion = 1, canSetSoldOut = CanAdjust(principal, identity, permissions) });
    }
}
