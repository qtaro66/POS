using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeDataSyncStatusPrincipalHttpHandler
{
    public static async Task<Results<
        Ok<BranchEmployeeDataSyncStatusResponse>,
        UnauthorizedHttpResult>> HandleAsync(
        ClaimsPrincipal? principal,
        IBranchEmployeeDataSyncStatusQuery query,
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

        return await BranchEmployeeDataSyncStatusHttpHandler.HandleAsync(
            identity,
            query,
            cancellationToken);
    }
}
