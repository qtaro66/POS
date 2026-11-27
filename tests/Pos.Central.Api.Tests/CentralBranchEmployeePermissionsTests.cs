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
    [Theory]
    [InlineData(null, 200, false)]
    [InlineData("true", 200, true)]
    [InlineData("false", 200, false)]
    [InlineData("invalid", 503, false)]
    public void Open_store_permission_is_optional_and_invalid_configuration_fails_closed(string? opening, int expected, bool permitted)
    {
        var prefix = CentralBranchEmployeePermissions.Source + ":0";
        var values = new Dictionary<string, string?>
        {
            [prefix + ":BranchId"] = "B1",
            [prefix + ":Revision"] = "1",
            [prefix + ":Employees:0:EmployeeId"] = "E1",
            [prefix + ":Employees:0:Active"] = "true",
            [prefix + ":Employees:0:SetSoldOut"] = "false",
        };
        if (opening is not null) { values[prefix + ":Employees:0:OpenStore"] = opening; }
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(CentralCloudClaimTypes.BranchId, "B1")], CentralBranchCertificateAuthentication.SchemeName));
        var result = CentralBranchEmployeePermissions.Read(context, principal, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        if (expected == 200)
        {
            var snapshot = Assert.IsType<BranchEmployeePermissionSnapshot>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
            Assert.Equal(permitted, Assert.Single(snapshot.Employees).OpenStore);
        }
    }

    [Theory]
    [InlineData(null, true, 200, false)]
    [InlineData("true", true, 200, true)]
    [InlineData("false", true, 200, false)]
    [InlineData("true", false, 200, false)]
    [InlineData("invalid", true, 503, false)]
    [InlineData("", true, 503, false)]
    public void Kitchen_reprint_permission_is_optional_scoped_and_suppressed_when_inactive(
        string? permission, bool active, int expected, bool permitted)
    {
        var source = CentralBranchEmployeePermissions.Source;
        var prefix = source + ":0";
        var values = new Dictionary<string, string?>
        {
            [prefix + ":BranchId"] = "B1",
            [prefix + ":Revision"] = "2",
            [prefix + ":Employees:0:EmployeeId"] = "E1",
            [prefix + ":Employees:0:Active"] = active.ToString(),
            [prefix + ":Employees:0:SetSoldOut"] = "true",
            [prefix + ":Employees:0:OpenStore"] = "true",
            [source + ":1:BranchId"] = "B2",
            [source + ":1:Revision"] = "99",
            [source + ":1:Employees:0:EmployeeId"] = "E1",
            [source + ":1:Employees:0:Active"] = "true",
            [source + ":1:Employees:0:SetSoldOut"] = "false",
            [source + ":1:Employees:0:ReprintKitchenOrder"] = "true"
        };
        if (permission is not null) { values[prefix + ":Employees:0:ReprintKitchenOrder"] = permission; }
        var context = new DefaultHttpContext(); context.Request.Scheme = "https";
        context.Request.QueryString = new QueryString("?branchId=B2");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(CentralCloudClaimTypes.BranchId, "B1")], CentralBranchCertificateAuthentication.SchemeName));
        var result = CentralBranchEmployeePermissions.Read(context, principal,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        if (expected == 200)
        {
            var snapshot = Assert.IsType<BranchEmployeePermissionSnapshot>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
            Assert.Equal(1, snapshot.SchemaVersion); Assert.Equal("B1", snapshot.BranchId); Assert.Equal(2, snapshot.Revision);
            var employee = Assert.Single(snapshot.Employees);
            Assert.Equal(permitted, employee.ReprintKitchenOrder);
            Assert.Equal(active, employee.SetSoldOut); Assert.Equal(active, employee.OpenStore);
        }
    }
}
