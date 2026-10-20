using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchPairingIssuancePrincipalHttpHandler
{
    public static async Task<Results<
        Ok<BranchPairingChallengeResponse>,
        UnauthorizedHttpResult>> HandleAsync(
        ClaimsPrincipal? principal,
        IOptions<BranchPairingOptions> pairingOptions,
        BranchPairingTokenIssuanceService issuanceService,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        BranchPairingSecretResponseHeaders.Apply(httpResponse);
        ArgumentNullException.ThrowIfNull(pairingOptions);
        ArgumentNullException.ThrowIfNull(issuanceService);

        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(
            principal,
            out var identity))
        {
            return TypedResults.Unauthorized();
        }

        return await BranchPairingIssuanceHttpHandler.HandleAsync(
            identity,
            pairingOptions,
            issuanceService,
            httpResponse,
            cancellationToken);
    }
}
