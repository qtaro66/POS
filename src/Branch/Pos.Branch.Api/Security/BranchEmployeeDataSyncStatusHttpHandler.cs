using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeDataSyncStatusHttpHandler
{
    public static async Task<Ok<BranchEmployeeDataSyncStatusResponse>> HandleAsync(
        AuthenticatedBranchRequestIdentity identity,
        IBranchEmployeeDataSyncStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(query);

        var status = await query.FindLatestAsync(
            identity.BranchId,
            cancellationToken);
        var response = BranchEmployeeDataSyncStatusResponseMapper.Map(
            identity.BranchId,
            status);

        return TypedResults.Ok(response);
    }
}
