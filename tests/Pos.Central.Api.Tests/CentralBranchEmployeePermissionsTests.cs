using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Central.Api.Tests;

public sealed class CentralBranchEmployeePermissionsTests
{
    [Fact]
    public void Uses_authenticated_branch_and_suppresses_permissions_for_inactive_employees()
    {
        var source = CentralBranchEmployeePermissions.Source;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{source}:0:BranchId"] = "B1",
            [$"{source}:0:Revision"] = "2",
            [$"{source}:0:Employees:0:EmployeeId"] = "E1",
            [$"{source}:0:Employees:0:Active"] = "false",
            [$"{source}:0:Employees:0:SetSoldOut"] = "true",
            [$"{source}:1:BranchId"] = "B2",
            [$"{source}:1:Revision"] = "4"
        }).Build();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.QueryString = new QueryString("?branchId=B2");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(CentralCloudClaimTypes.BranchId, "B1")], CentralBranchCertificateAuthentication.SchemeName));

        var result = CentralBranchEmployeePermissions.Read(context, principal, configuration);

        var value = Assert.IsType<BranchEmployeePermissionSnapshot>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal("B1", value.BranchId);
        Assert.Equal(2, value.Revision);
        Assert.Equal(new BranchEmployeePermissionEntry("E1", false, false), Assert.Single(value.Employees));
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }
}
