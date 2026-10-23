using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Security;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudDataSentStatusPrincipalHttpHandler
{
    public static async Task<Results<
        Ok<BranchCloudDataSentStatusResponse>,
        UnauthorizedHttpResult>> HandleAsync(
        ClaimsPrincipal? principal,
        IBranchCloudDataSentStatusQuery query,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(httpResponse);
        BranchSecretResponseHeaders.Apply(httpResponse);

        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(
                principal,
                out var identity))
        {
            return TypedResults.Unauthorized();
        }

        return await BranchCloudDataSentStatusHttpHandler.HandleAsync(
            identity,
            query,
            cancellationToken);
    }
}
