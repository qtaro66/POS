using Microsoft.AspNetCore.Authorization;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralCloudIngestionAuthorizationRequirement
    : IAuthorizationRequirement;

public sealed class CentralCloudIngestionAuthorizationHandler
    : AuthorizationHandler<CentralCloudIngestionAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CentralCloudIngestionAuthorizationRequirement requirement)
    {
        if (AuthenticatedCentralBranchIdentityResolver.TryResolve(
                context.User,
                out _))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
