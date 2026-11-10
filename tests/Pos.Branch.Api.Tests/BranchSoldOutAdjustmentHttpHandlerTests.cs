using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchSoldOutAdjustmentHttpHandlerTests
{
    [Theory]
    [InlineData(null, null, true, 403)]
    [InlineData("Mobile", null, true, 403)]
    [InlineData(null, "PcPos", true, 403)]
    [InlineData("PcPos", null, false, 401)]
    [InlineData("PcPos", null, true, 503)]
    public async Task Only_provisioned_pc_pos_with_employee_identity_reaches_the_store(
        string? deviceRole, string? unrelatedRole, bool employeePresent, int expectedStatus)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
        var runtime = new BranchOrderingRuntime(path, TimeProvider.System);
        var result = await BranchSoldOutAdjustmentHttpHandler.HandleAsync(
            Principal(deviceRole, unrelatedRole, employeePresent), new(Guid.NewGuid(), "ITEM-01", false, true),
            runtime, CancellationToken.None);
        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData("ITEM-01", null, true)]
    [InlineData("ITEM-01", false, false)]
    public async Task Rejects_missing_or_unchanged_input(string? item, bool? expected, bool? desired)
    {
        var result = await BranchSoldOutAdjustmentHttpHandler.HandleAsync(
            Principal("PcPos", null, true), new(Guid.NewGuid(), item, expected, desired),
            new BranchOrderingRuntime("unused.db", TimeProvider.System), CancellationToken.None);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    internal static ClaimsPrincipal Principal(string? deviceRole, string? unrelatedRole, bool employeePresent)
    {
        var employee = new ClaimsIdentity(
            [new(BranchPosClaimTypes.BranchId, "B1"), new(BranchPosClaimTypes.DeviceId, "DEVICE1")],
            BranchEmployeeSessionAuthenticationDefaults.SchemeName);
        if (deviceRole is not null) { employee.AddClaim(new(BranchPosClaimTypes.DeviceRole, deviceRole)); }
        if (employeePresent) { employee.AddClaim(new(BranchPosClaimTypes.EmployeeId, "EMP1")); }
        var principal = new ClaimsPrincipal(employee);
        if (unrelatedRole is not null)
        {
            principal.AddIdentity(new ClaimsIdentity([new(BranchPosClaimTypes.DeviceRole, unrelatedRole)], "Unrelated"));
        }
        return principal;
    }
}
