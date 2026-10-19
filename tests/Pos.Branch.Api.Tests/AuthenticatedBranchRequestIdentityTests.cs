using System.Security.Claims;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class AuthenticatedBranchRequestIdentityTests
{
    [Fact]
    public void Authenticated_principal_with_exact_claims_resolves_identity()
    {
        var principal = Principal(
            Claim(BranchPosClaimTypes.BranchId, " BRANCH-01 "),
            Claim(BranchPosClaimTypes.DeviceId, " DEVICE-01 "),
            Claim(BranchPosClaimTypes.EmployeeId, " EMPLOYEE-01 "));

        var resolved = AuthenticatedBranchRequestIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.True(resolved);
        Assert.NotNull(identity);
        Assert.Equal("BRANCH-01", identity.BranchId);
        Assert.Equal("DEVICE-01", identity.DeviceId);
        Assert.Equal("EMPLOYEE-01", identity.EmployeeId);
    }

    [Fact]
    public void Anonymous_principal_is_rejected()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
                Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
                Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01")
            ]));

        Assert.False(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                principal,
                out var identity));
        Assert.Null(identity);
    }

    [Theory]
    [InlineData(BranchPosClaimTypes.BranchId)]
    [InlineData(BranchPosClaimTypes.DeviceId)]
    [InlineData(BranchPosClaimTypes.EmployeeId)]
    public void Missing_required_claim_is_rejected(string missingClaimType)
    {
        var claims = RequiredClaims()
            .Where(claim => !string.Equals(
                claim.Type,
                missingClaimType,
                StringComparison.Ordinal))
            .ToArray();

        Assert.False(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                Principal(claims),
                out var identity));
        Assert.Null(identity);
    }

    [Theory]
    [InlineData(BranchPosClaimTypes.BranchId)]
    [InlineData(BranchPosClaimTypes.DeviceId)]
    [InlineData(BranchPosClaimTypes.EmployeeId)]
    public void Duplicate_required_claim_is_rejected(string duplicateClaimType)
    {
        var claims = RequiredClaims()
            .Append(Claim(duplicateClaimType, "DUPLICATE"))
            .ToArray();

        Assert.False(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                Principal(claims),
                out var identity));
        Assert.Null(identity);
    }

    [Theory]
    [InlineData(BranchPosClaimTypes.BranchId)]
    [InlineData(BranchPosClaimTypes.DeviceId)]
    [InlineData(BranchPosClaimTypes.EmployeeId)]
    public void Whitespace_required_claim_is_rejected(string emptyClaimType)
    {
        var claims = RequiredClaims()
            .Select(claim => string.Equals(
                claim.Type,
                emptyClaimType,
                StringComparison.Ordinal)
                ? Claim(claim.Type, " ")
                : claim)
            .ToArray();

        Assert.False(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                Principal(claims),
                out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void Claims_from_anonymous_identity_do_not_complete_authenticated_identity()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
            [Claim(BranchPosClaimTypes.BranchId, "BRANCH-01")],
            "TestAuthentication"),
            new ClaimsIdentity(
            [
                Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
                Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01")
            ])
        ]);

        Assert.False(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                principal,
                out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void Null_principal_is_rejected()
    {
        Assert.False(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                null,
                out var identity));
        Assert.Null(identity);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuthentication"));

    private static Claim[] RequiredClaims() =>
    [
        Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
        Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
        Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01")
    ];

    private static Claim Claim(string type, string value) => new(type, value);
}
