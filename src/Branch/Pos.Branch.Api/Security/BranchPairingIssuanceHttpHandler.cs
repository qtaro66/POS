using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchPairingIssuanceHttpHandler
{
    public static async Task<Ok<BranchPairingChallengeResponse>> HandleAsync(
        AuthenticatedBranchRequestIdentity identity,
        IOptions<BranchPairingOptions> pairingOptions,
        BranchPairingTokenIssuanceService issuanceService,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        BranchSecretResponseHeaders.Apply(httpResponse);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(pairingOptions);
        ArgumentNullException.ThrowIfNull(issuanceService);

        var serviceOrigin = pairingOptions.Value.ServiceOrigin;
        BranchPairingPhaseZeroServiceOrigin.Require(
            serviceOrigin,
            nameof(pairingOptions));

        var challenge = await issuanceService.IssueAsync(
            identity.BranchId,
            identity.EmployeeId,
            cancellationToken);
        var response = BranchPairingChallengeResponseMapper.Map(
            serviceOrigin,
            identity.BranchId,
            challenge);

        return TypedResults.Ok(response);
    }
}
