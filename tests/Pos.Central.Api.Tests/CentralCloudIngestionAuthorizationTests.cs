using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class CentralCloudIngestionAuthorizationTests
{
    [Fact]
    public async Task Single_authenticated_branch_identity_is_authorized()
    {
        var context = await AuthorizeAsync(Principal(
            authenticated: true,
            Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01")));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Missing_branch_identity_is_rejected()
    {
        var context = await AuthorizeAsync(Principal(
            authenticated: true,
            Claim(ClaimTypes.NameIdentifier, "DEVICE-01")));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Duplicate_branch_identity_is_rejected()
    {
        var context = await AuthorizeAsync(Principal(
            authenticated: true,
            Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01"),
            Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01")));

        Assert.False(context.HasSucceeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Empty_branch_identity_is_rejected(string branchId)
    {
        var context = await AuthorizeAsync(Principal(
            authenticated: true,
            Claim(CentralCloudClaimTypes.BranchId, branchId)));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Unauthenticated_branch_identity_is_rejected()
    {
        var context = await AuthorizeAsync(Principal(
            authenticated: false,
            Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01")));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Branch_claim_type_matching_is_case_sensitive()
    {
        var context = await AuthorizeAsync(Principal(
            authenticated: true,
            Claim("POS_BRANCH_ID", "BRANCH-01")));

        Assert.False(context.HasSucceeded);
    }

    private static Claim Claim(string type, string value) => new(type, value);

    private static ClaimsPrincipal Principal(
        bool authenticated,
        params Claim[] claims) =>
        new(new ClaimsIdentity(
            claims,
            authenticated ? "SyntheticAuthentication" : null));

    private static async Task<AuthorizationHandlerContext> AuthorizeAsync(
        ClaimsPrincipal principal)
    {
        var requirement = new CentralCloudIngestionAuthorizationRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            principal,
            resource: null);
        await new CentralCloudIngestionAuthorizationHandler().HandleAsync(context);
        return context;
    }
}
