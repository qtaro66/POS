using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Pos.Application.Security;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private sealed class LifecyclePins : IBranchEmployeePinValidator
    {
        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(string branchId, ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(pin.Span.SequenceEqual("111111") ? new VerifiedBranchEmployee(branchId, "EMP-2") :
                pin.Span.SequenceEqual("222222") ? new VerifiedBranchEmployee(branchId, "EMP-3") : null);
    }

    private static ClaimsPrincipal LifecyclePrincipal(string role = "PcPos", string branch = "BRANCH-01") =>
        new(new ClaimsIdentity([
            new Claim(BranchPosClaimTypes.BranchId, branch),
            new Claim(BranchPosClaimTypes.DeviceId, "DEV-1"),
            new Claim(BranchPosClaimTypes.EmployeeId, "EMP-1"),
            new Claim(BranchPosClaimTypes.DeviceRole, role)
        ], BranchEmployeeSessionAuthenticationDefaults.SchemeName));

    [Fact]
    public async Task Current_lifecycle_recovers_latest_store_on_each_branch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = LifecyclePrincipal();
        Assert.Equal(404, (await fixture.Call("GetCurrentStoreLifecycle", principal: principal)).Status);
        Assert.Equal(403, (await fixture.Call("GetCurrentStoreLifecycle", principal: LifecyclePrincipal("Mobile"))).Status);
        var request = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 0, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", request, principal: principal)).Status);
        var current = await fixture.Call("GetCurrentStoreLifecycle", principal: principal);
        Assert.Equal(200, current.Status);
        Assert.Equal(request.StoreSessionId, current.Body.GetProperty("storeSessionId").GetString());
        Assert.Equal("no-store", current.CacheControl);
        Assert.Equal(404, (await fixture.Call("GetCurrentStoreLifecycle", principal: LifecyclePrincipal(branch: "OTHER"))).Status);
        var close = request with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore" };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: principal)).Status);
        var next = request with
        {
            RequestId = Guid.NewGuid().ToString(),
            StoreSessionId = Guid.NewGuid().ToString(),
            ShiftId = Guid.NewGuid().ToString()
        };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", next, principal: principal)).Status);
        var latest = await fixture.Call("GetCurrentStoreLifecycle", principal: principal);
        Assert.Equal(next.StoreSessionId, latest.Body.GetProperty("storeSessionId").GetString());
    }
    [Fact]
    public async Task Lifecycle_routes_use_pinned_employee_and_replay_across_server_time_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = Guid.NewGuid().ToString();
        var shift = Guid.NewGuid().ToString();
        var principal = LifecyclePrincipal();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), store, shift, "OpenStore", 500, "111111");

        Assert.Equal(403, (await fixture.Call("ExecuteStoreLifecycle", open)).Status);
        Assert.Equal(403, (await fixture.Call("GetStoreLifecycle", routeValue: store, principal: new ClaimsPrincipal())).Status);
        Assert.Equal(403, (await fixture.Call("ExecuteStoreLifecycle", open, principal: LifecyclePrincipal("Mobile"))).Status);
        Assert.Equal(401, (await fixture.Call("ExecuteStoreLifecycle", open with { Pin = "999999" }, principal: principal)).Status);
        Assert.Equal(403, (await fixture.Call("ExecuteStoreLifecycle", open with { Pin = "222222" }, principal: principal)).Status);
        var first = await fixture.Call("ExecuteStoreLifecycle", open, principal: principal);
        Assert.Equal(200, first.Status);
        await Task.Delay(20);
        var replay = await fixture.Call("ExecuteStoreLifecycle", open, principal: principal);
        Assert.Equal(first.Body.GetRawText(), replay.Body.GetRawText());
        Assert.Equal(409, (await fixture.Call("ExecuteStoreLifecycle", open with { CashAmount = 600 }, principal: principal)).Status);
        var audit = await fixture.Scalar("SELECT command_json FROM store_lifecycle_commands");
        Assert.Contains("EMP-2", audit);
        Assert.DoesNotContain("111111", audit);
        Assert.DoesNotContain("Pin", audit);
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM store_lifecycle_commands"));
        var status = await fixture.Call("GetStoreLifecycle", routeValue: store, principal: principal);
        Assert.Equal(200, status.Status);
        Assert.Equal("no-store", status.CacheControl);
        Assert.Equal(404, (await fixture.Call("GetStoreLifecycle", routeValue: store, principal: LifecyclePrincipal(branch: "BRANCH-02"))).Status);

        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseShift", CashAmount = null, Pin = "222222" };
        var closed = await fixture.Call("ExecuteStoreLifecycle", close, principal: principal);
        Assert.Equal(200, closed.Status);
        Assert.Equal("EMP-3", closed.Body.GetProperty("closing").GetProperty("context").GetProperty("employeeId").GetString());
        Assert.Equal(JsonValueKind.Null, closed.Body.GetProperty("closing").GetProperty("actualCashAmount").ValueKind);
        var next = open with { RequestId = Guid.NewGuid().ToString(), ShiftId = Guid.NewGuid().ToString(), Operation = "OpenShift", CashAmount = null, Pin = "222222" };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", next, principal: principal)).Status);
        var end = next with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 490 };
        var ended = await fixture.Call("ExecuteStoreLifecycle", end, principal: principal);
        Assert.Equal(200, ended.Status);
        Assert.Equal(-10, ended.Body.GetProperty("closing").GetProperty("cashDifference").GetDecimal());
    }

    [Theory]
    [InlineData("OpenStore", null)]
    [InlineData("CloseStore", null)]
    [InlineData("OpenShift", -1)]
    [InlineData("0", 0)]
    public async Task Lifecycle_routes_reject_invalid_operation_or_cash(string operation, int? cash)
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), operation, cash, "111111");
        Assert.Equal(400, (await fixture.Call("ExecuteStoreLifecycle", request, principal: LifecyclePrincipal())).Status);
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM store_lifecycle_commands"));
        Assert.Equal(400, (await fixture.Call("ExecuteStoreLifecycle", request with { Operation = "OpenStore", CashAmount = 1.001m }, principal: LifecyclePrincipal())).Status);
    }
}

public sealed class StoreOpeningPermissionTests
{
    [Fact]
    public async Task Old_snapshots_default_to_denied_and_open_store_permission_is_branch_scoped()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var cache = new Pos.Branch.Api.Synchronization.BranchEmployeePermissionCache("B1", path);
            using var old = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"branchId":"B1","revision":1,"employees":[{"employeeId":"E1","active":true,"setSoldOut":true}]}"""));
            Assert.True(await cache.ApplyAsync(old, default));
            Assert.False(cache.CanOpenStore("B1", "E1"));
            using var current = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"branchId":"B1","revision":2,"employees":[{"employeeId":"E1","active":true,"setSoldOut":false,"openStore":true}]}"""));
            Assert.True(await cache.ApplyAsync(current, default));
            Assert.True(cache.CanOpenStore("B1", "E1"));
            Assert.False(cache.CanOpenStore("B2", "E1"));
            var restored = new Pos.Branch.Api.Synchronization.BranchEmployeePermissionCache("B1", path);
            await restored.RestoreAsync(default);
            Assert.True(restored.CanOpenStore("B1", "E1"));
        }
        finally { File.Delete(path); }
    }
}
