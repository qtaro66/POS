using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCompleteIdentityAuthorizationTests
{
    [Fact]
    public async Task Complete_authenticated_identity_is_authorized()
    {
        var context = await AuthorizeAsync(
            Principal(
                new Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
                new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01")));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Missing_employee_identity_is_rejected()
    {
        var context = await AuthorizeAsync(
            Principal(
                new Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01")));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Duplicate_identity_claim_is_rejected()
    {
        var context = await AuthorizeAsync(
            Principal(
                new Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
                new Claim(BranchPosClaimTypes.BranchId, "BRANCH-02"),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
                new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01")));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Unauthenticated_identity_is_rejected()
    {
        var context = await AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
                new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01")
            ])));

        Assert.False(context.HasSucceeded);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Synthetic"));

    private static async Task<AuthorizationHandlerContext> AuthorizeAsync(
        ClaimsPrincipal principal)
    {
        var requirement = new BranchCompleteIdentityAuthorizationRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            principal,
            resource: null);
        await new BranchCompleteIdentityAuthorizationHandler().HandleAsync(context);
        return context;
    }
}
