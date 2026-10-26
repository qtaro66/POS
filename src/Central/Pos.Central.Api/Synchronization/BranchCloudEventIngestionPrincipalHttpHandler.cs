using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Central.Api.Synchronization;

public static class BranchCloudEventIngestionPrincipalHttpHandler
{
    public static async Task<Results<
        Ok<BranchCloudEventIngestionResponse>,
        UnauthorizedHttpResult>> HandleAsync(
        ClaimsPrincipal? principal,
        BranchCloudEventIngestionRequest request,
        BranchCloudEventIngestionService service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(service);

        if (!AuthenticatedCentralBranchIdentityResolver.TryResolve(
                principal,
                out var identity))
        {
            return TypedResults.Unauthorized();
        }

        var response = await BranchCloudEventIngestionIdentityHandler.HandleAsync(
            request,
            identity,
            service,
            cancellationToken);
        return TypedResults.Ok(response);
    }
}
