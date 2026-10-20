using Microsoft.AspNetCore.Authorization;

namespace Pos.Branch.Api.Security;

public static class BranchPosAuthorizationDefaults
{
    public const string CompleteIdentityPolicyName = "BranchPosAuthenticated";
}

public sealed class BranchCompleteIdentityAuthorizationRequirement : IAuthorizationRequirement;

public sealed class BranchCompleteIdentityAuthorizationHandler
    : AuthorizationHandler<BranchCompleteIdentityAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchCompleteIdentityAuthorizationRequirement requirement)
    {
        if (AuthenticatedBranchRequestIdentityResolver.TryResolve(context.User, out _))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
