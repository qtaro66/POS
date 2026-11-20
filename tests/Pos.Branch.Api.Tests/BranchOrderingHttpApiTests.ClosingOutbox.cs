using System.Security.Claims;
using Pos.Branch.Api.Ordering;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Fact]
    public async Task Closing_outbox_status_is_branch_scoped_and_excludes_report_payload()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = LifecyclePrincipal();
        var empty = await fixture.Call("GetStoreClosingOutbox", principal: principal);
        Assert.Equal(200, empty.Status);
        Assert.Equal(0, empty.Body.GetProperty("pendingCount").GetInt32());
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 500, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: principal)).Status);
        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 490 };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: principal)).Status);
        var status = await fixture.Call("GetStoreClosingOutbox", principal: principal);
        Assert.Equal(200, status.Status);
        Assert.Equal("no-store", status.CacheControl);
        Assert.Equal("BRANCH-01", status.Body.GetProperty("branchId").GetString());
        Assert.Equal(1, status.Body.GetProperty("pendingCount").GetInt32());
        Assert.Equal(0, status.Body.GetProperty("leasedCount").GetInt32());
        Assert.Equal(0, status.Body.GetProperty("deliveredCount").GetInt32());
        Assert.Equal(5, status.Body.EnumerateObject().Count());
        Assert.DoesNotContain(close.RequestId, status.Body.GetRawText());
        var other = await fixture.Call("GetStoreClosingOutbox", principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(200, other.Status);
        Assert.Equal(0, other.Body.GetProperty("pendingCount").GetInt32());
        Assert.Equal("OTHER", other.Body.GetProperty("branchId").GetString());
    }

    [Theory]
    [InlineData("Mobile")]
    [InlineData("Kitchen")]
    public async Task Closing_outbox_status_requires_pc_pos_role(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(403, (await fixture.Call("GetStoreClosingOutbox", principal: LifecyclePrincipal(role))).Status);
        Assert.Equal(403, (await fixture.Call("GetStoreClosingOutbox", principal: new ClaimsPrincipal())).Status);
    }

    [Fact]
    public async Task Closing_outbox_status_returns_unavailable_when_storage_is_missing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Execute("DROP TABLE closing_report_outbox;");
        var response = await fixture.Call("GetStoreClosingOutbox", principal: LifecyclePrincipal());
        Assert.Equal(503, response.Status);
        Assert.Equal("no-store", response.CacheControl);
    }
}
