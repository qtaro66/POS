using System.Security.Claims;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class AuthenticatedCentralBranchIdentityResolverTests
{
    [Fact]
    public void Single_branch_claim_on_authenticated_identity_is_resolved()
    {
        var principal = Principal(
            authenticated: true,
            Claim(CentralCloudClaimTypes.BranchId, " BRANCH-01 "));

        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.True(resolved);
        Assert.NotNull(identity);
        Assert.Equal("BRANCH-01", identity.BranchId);
    }

    [Fact]
    public void Null_principal_is_rejected()
    {
        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            null,
            out var identity);

        Assert.False(resolved);
        Assert.Null(identity);
    }

    [Fact]
    public void Unauthenticated_identity_is_rejected()
    {
        var principal = Principal(
            authenticated: false,
            Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01"));

        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.False(resolved);
        Assert.Null(identity);
    }

    [Fact]
    public void Claim_on_unauthenticated_identity_is_ignored()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
                [Claim(CentralCloudClaimTypes.BranchId, "UNTRUSTED-BRANCH")]),
            new ClaimsIdentity(
                [Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01")],
                "SyntheticAuthentication")
        ]);

        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.True(resolved);
        Assert.NotNull(identity);
        Assert.Equal("BRANCH-01", identity.BranchId);
    }

    [Fact]
    public void Missing_branch_claim_is_rejected()
    {
        var principal = Principal(
            authenticated: true,
            Claim(ClaimTypes.NameIdentifier, "DEVICE-01"));

        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.False(resolved);
        Assert.Null(identity);
    }

    [Fact]
    public void Duplicate_branch_claims_are_rejected_even_when_values_match()
    {
        var principal = Principal(
            authenticated: true,
            Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01"),
            Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01"));

        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.False(resolved);
        Assert.Null(identity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_branch_claim_is_rejected(string branchId)
    {
        var principal = Principal(
            authenticated: true,
            Claim(CentralCloudClaimTypes.BranchId, branchId));

        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.False(resolved);
        Assert.Null(identity);
    }

    [Fact]
    public void Claim_type_matching_is_ordinal_and_case_sensitive()
    {
        var principal = Principal(
            authenticated: true,
            Claim("POS_BRANCH_ID", "BRANCH-01"));

        var resolved = AuthenticatedCentralBranchIdentityResolver.TryResolve(
            principal,
            out var identity);

        Assert.False(resolved);
        Assert.Null(identity);
    }

    private static Claim Claim(string type, string value) => new(type, value);

    private static ClaimsPrincipal Principal(
        bool authenticated,
        params Claim[] claims) =>
        new(new ClaimsIdentity(
            claims,
            authenticated ? "SyntheticAuthentication" : null));
}
