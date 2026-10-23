using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Security;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudDataSentStatusHttpHandler
{
    public static async Task<Ok<BranchCloudDataSentStatusResponse>> HandleAsync(
        AuthenticatedBranchRequestIdentity identity,
        IBranchCloudDataSentStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(query);

        var status = await query.FindLatestAsync(
            identity.BranchId,
            cancellationToken);
        var response = BranchCloudDataSentStatusResponseMapper.Map(
            identity.BranchId,
            status);

        return TypedResults.Ok(response);
    }
}
