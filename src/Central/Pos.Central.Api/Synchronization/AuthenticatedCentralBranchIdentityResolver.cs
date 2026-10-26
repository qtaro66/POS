using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Pos.Central.Api.Synchronization;

public static class CentralCloudClaimTypes
{
    public const string BranchId = "pos_branch_id";
}

public static class AuthenticatedCentralBranchIdentityResolver
{
    public static bool TryResolve(
        ClaimsPrincipal? principal,
        [NotNullWhen(true)] out AuthenticatedCentralBranchIdentity? identity)
    {
        identity = null;
        if (principal is null)
        {
            return false;
        }

        var branchIds = principal.Identities
            .Where(candidate => candidate.IsAuthenticated)
            .SelectMany(candidate => candidate.Claims)
            .Where(claim => string.Equals(
                claim.Type,
                CentralCloudClaimTypes.BranchId,
                StringComparison.Ordinal))
            .Select(claim => claim.Value?.Trim())
            .ToArray();

        if (branchIds.Length != 1 || string.IsNullOrWhiteSpace(branchIds[0]))
        {
            return false;
        }

        identity = AuthenticatedCentralBranchIdentity.Create(branchIds[0]!);
        return true;
    }
}
