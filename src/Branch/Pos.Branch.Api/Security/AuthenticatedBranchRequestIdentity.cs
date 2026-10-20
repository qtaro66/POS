using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Pos.Branch.Api.Security;

public static class BranchPosClaimTypes
{
    public const string BranchId = "pos_branch_id";
    public const string DeviceId = "pos_device_id";
    public const string EmployeeId = "pos_employee_id";
}

public sealed class AuthenticatedBranchRequestIdentity
{
    internal AuthenticatedBranchRequestIdentity(
        string branchId,
        string deviceId,
        string employeeId)
    {
        BranchId = branchId;
        DeviceId = deviceId;
        EmployeeId = employeeId;
    }

    public string BranchId { get; }

    public string DeviceId { get; }

    public string EmployeeId { get; }
}

public static class AuthenticatedBranchRequestIdentityResolver
{
    public static bool TryResolve(
        ClaimsPrincipal? principal,
        [NotNullWhen(true)] out AuthenticatedBranchRequestIdentity? identity)
    {
        identity = null;
        if (principal is null)
        {
            return false;
        }

        var authenticatedClaims = principal.Identities
            .Where(candidate => candidate.IsAuthenticated)
            .SelectMany(candidate => candidate.Claims)
            .ToArray();
        if (!TryGetSingle(authenticatedClaims, BranchPosClaimTypes.BranchId, out var branchId) ||
            !TryGetSingle(authenticatedClaims, BranchPosClaimTypes.DeviceId, out var deviceId) ||
            !TryGetSingle(authenticatedClaims, BranchPosClaimTypes.EmployeeId, out var employeeId))
        {
            return false;
        }

        identity = new AuthenticatedBranchRequestIdentity(branchId, deviceId, employeeId);
        return true;
    }

    private static bool TryGetSingle(
        IEnumerable<Claim> claims,
        string claimType,
        out string value)
    {
        value = string.Empty;
        var matches = claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .Select(claim => claim.Value?.Trim())
            .ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0]))
        {
            return false;
        }

        value = matches[0]!;
        return true;
    }
}
