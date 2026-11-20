using Pos.Branch.Api.Ordering;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Fact]
    public async Task Lifecycle_workspace_and_closing_are_read_only_pin_free_and_branch_scoped()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = LifecyclePrincipal();
        var empty = await fixture.Call("GetStoreLifecycleWorkspace", principal: principal);
        Assert.Equal(200, empty.Status);
        Assert.Empty(empty.Body.GetProperty("stores").EnumerateArray());
        Assert.Equal("no-store", empty.CacheControl);
        Assert.Equal(403, (await fixture.Call("GetStoreLifecycleWorkspace", principal: LifecyclePrincipal("Mobile"))).Status);
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 500, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: principal)).Status);
        Assert.Equal(404, (await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: open.ShiftId, principal: principal)).Status);
        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseShift", CashAmount = null };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: principal)).Status);
        var workspace = await fixture.Call("GetStoreLifecycleWorkspace", principal: principal);
        Assert.Equal(200, workspace.Status);
        Assert.Equal("BRANCH-01", workspace.Body.GetProperty("branchId").GetString());
        Assert.True(DateTimeOffset.TryParse(workspace.Body.GetProperty("asOf").GetString(), out _));
        var store = Assert.Single(workspace.Body.GetProperty("stores").EnumerateArray());
        Assert.Equal("CashSales", workspace.Body.GetProperty("cashSummary").GetProperty("source").GetString());
        Assert.Equal(0, workspace.Body.GetProperty("cashSummary").GetProperty("store").GetProperty("receiptCount").GetInt32());
        Assert.Equal(open.StoreSessionId, store.GetProperty("storeSessionId").GetString());
        Assert.Equal("EMP-2", store.GetProperty("opened").GetProperty("employeeId").GetString());
        Assert.Single(store.GetProperty("shifts").EnumerateArray());
        var snapshot = await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: open.ShiftId, principal: principal);
        Assert.Equal(200, snapshot.Status);
        Assert.Equal("no-store", snapshot.CacheControl);
        Assert.Equal("NotReconciled", snapshot.Body.GetProperty("closing").GetProperty("reconciliationStatus").GetString());
        Assert.Equal(500, snapshot.Body.GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal("CashSales", snapshot.Body.GetProperty("cashSummary").GetProperty("source").GetString());
        Assert.Equal(0, snapshot.Body.GetProperty("cashSummary").GetProperty("shift").GetProperty("appliedCashAmount").GetDecimal());
        Assert.Equal(404, (await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: open.ShiftId, principal: LifecyclePrincipal(branch: "OTHER"))).Status);
        Assert.Empty((await fixture.Call("GetStoreLifecycleWorkspace", principal: LifecyclePrincipal(branch: "OTHER")))
            .Body.GetProperty("stores").EnumerateArray());
        Assert.Equal(400, (await fixture.Call("GetStoreShiftClosing", routeValue: "invalid",
            shiftId: open.ShiftId, principal: principal)).Status);
        Assert.Equal("2", await fixture.Scalar("SELECT count(*) FROM store_lifecycle_commands"));
    }
}
