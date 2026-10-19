using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchPairingIssuanceHttpHandler
{
    public static async Task<Ok<BranchPairingChallengeResponse>> HandleAsync(
        AuthenticatedBranchRequestIdentity identity,
        string branchServiceUrl,
        BranchPairingTokenIssuanceService issuanceService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(issuanceService);

        BranchPairingPhaseZeroServiceOrigin.Require(
            branchServiceUrl,
            nameof(branchServiceUrl));

        var challenge = await issuanceService.IssueAsync(
            identity.BranchId,
            identity.EmployeeId,
            cancellationToken);
        var response = BranchPairingChallengeResponseMapper.Map(
            branchServiceUrl,
            identity.BranchId,
            challenge);

        return TypedResults.Ok(response);
    }
}
