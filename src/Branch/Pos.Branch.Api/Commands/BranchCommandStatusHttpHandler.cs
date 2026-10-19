using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.TableSessions;
using Pos.Branch.Api.Security;
using Pos.Contracts.Commands;

namespace Pos.Branch.Api.Commands;

public static class BranchCommandStatusHttpHandler
{
    public static async Task<Results<
        Ok<BranchCommandStatusResponse>,
        NotFound<BranchCommandStatusResponse>>> HandleAsync(
        AuthenticatedBranchRequestIdentity identity,
        string requestId,
        IBranchCommandStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(query);
        requestId = RequireIdentifier(requestId, nameof(requestId));

        var receipt = await query.FindAsync(
            identity.BranchId,
            requestId,
            cancellationToken);
        var response = BranchCommandStatusResponseMapper.Map(
            identity.BranchId,
            requestId,
            receipt);

        return receipt is null
            ? TypedResults.NotFound(response)
            : TypedResults.Ok(response);
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
