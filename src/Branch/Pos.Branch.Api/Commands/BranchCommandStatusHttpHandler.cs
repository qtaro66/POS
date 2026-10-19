using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.TableSessions;
using Pos.Contracts.Commands;

namespace Pos.Branch.Api.Commands;

public static class BranchCommandStatusHttpHandler
{
    public static async Task<Results<
        Ok<BranchCommandStatusResponse>,
        NotFound<BranchCommandStatusResponse>>> HandleAsync(
        string authenticatedBranchId,
        string requestId,
        IBranchCommandStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        authenticatedBranchId = RequireIdentifier(
            authenticatedBranchId,
            nameof(authenticatedBranchId));
        requestId = RequireIdentifier(requestId, nameof(requestId));

        var receipt = await query.FindAsync(
            authenticatedBranchId,
            requestId,
            cancellationToken);
        var response = BranchCommandStatusResponseMapper.Map(
            authenticatedBranchId,
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
