using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Central.Api.Synchronization;

public static class BranchCloudEventIngestionPrincipalHttpHandler
{
    public static async Task<Results<
        Ok<BranchCloudEventIngestionResponse>,
        UnauthorizedHttpResult,
        ForbidHttpResult,
        StatusCodeHttpResult>> HandleAsync(
        ClaimsPrincipal? principal,
        BranchCloudEventIngestionRequest request,
        [FromServices] BranchCloudEventIngestionService service,
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

        try
        {
            var response = await BranchCloudEventIngestionIdentityHandler.HandleAsync(
                request,
                identity,
                service,
                cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (BranchCloudEventBranchMismatchException)
        {
            return TypedResults.Forbid();
        }
        catch (BranchCloudEventUniquenessConflictException)
        {
            return TypedResults.StatusCode(StatusCodes.Status409Conflict);
        }
        catch (BranchCloudEventPersistenceUnavailableException)
        {
            return TypedResults.StatusCode(
                StatusCodes.Status503ServiceUnavailable);
        }
    }
}
