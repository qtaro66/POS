using System.Security.Claims;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private static string RecoveryQuery(StoreLifecycleEndpoints.Request request) =>
        "?storeSessionId=" + request.StoreSessionId + "&shiftId=" + request.ShiftId +
        "&operation=" + request.Operation +
        (request.CashAmount is null ? "" : "&cashAmount=" + request.CashAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task Lifecycle_request_recovery_is_pin_free_read_and_active_pin_retirement_with_late_write_guard()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = LifecyclePrincipal();
        var pending = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 500, "111111");
        var query = RecoveryQuery(pending);
        var read = await fixture.Call("GetStoreLifecycleRequest", routeValue: pending.RequestId, principal: principal, query: query);
        Assert.Equal(200, read.Status);
        Assert.Equal("NotFound", read.Body.GetProperty("status").GetString());
        Assert.Equal("no-store", read.CacheControl);
        Assert.Equal(403, (await fixture.Call("GetStoreLifecycleRequest", routeValue: pending.RequestId,
            principal: LifecyclePrincipal("Mobile"), query: query)).Status);
        Assert.Equal(401, (await fixture.Call("RetireStoreLifecycleRequest", pending with { Pin = "999999" }, principal: principal)).Status);
        var retired = await fixture.Call("RetireStoreLifecycleRequest", pending with { Pin = "222222" }, principal: principal);
        Assert.Equal(200, retired.Status);
        Assert.Equal("Retired", retired.Body.GetProperty("status").GetString());
        Assert.Equal("409", (await fixture.Call("ExecuteStoreLifecycle", pending, principal: principal)).Status.ToString());
        Assert.Equal("EMP-3", await fixture.Scalar("SELECT retired_by_employee_id FROM store_lifecycle_retirements"));
        Assert.DoesNotContain("222222", (await fixture.Scalar("SELECT target_json FROM store_lifecycle_retirements"))!);
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM store_lifecycle_commands"));
        Assert.Equal("Retired", (await fixture.Call("GetStoreLifecycleRequest", routeValue: pending.RequestId,
            principal: principal, query: query)).Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Completed_request_read_and_retirement_return_original_result_for_same_device()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = LifecyclePrincipal();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 0, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: principal)).Status);
        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 0 };
        var saved = await fixture.Call("ExecuteStoreLifecycle", close, principal: principal);
        Assert.Equal(200, saved.Status);
        var query = RecoveryQuery(close);
        var read = await fixture.Call("GetStoreLifecycleRequest", routeValue: close.RequestId, principal: principal, query: query);
        Assert.Equal("Completed", read.Body.GetProperty("status").GetString());
        Assert.Equal(saved.Body.GetRawText(), read.Body.GetProperty("result").GetRawText());
        var retired = await fixture.Call("RetireStoreLifecycleRequest", close with { Pin = "222222" }, principal: principal);
        Assert.Equal(saved.Body.GetRawText(), retired.Body.GetProperty("result").GetRawText());
        var foreign = new ClaimsPrincipal(new ClaimsIdentity(principal.Claims.Where(c => c.Type != BranchPosClaimTypes.DeviceId)
            .Append(new Claim(BranchPosClaimTypes.DeviceId, "OTHER")), principal.Identity!.AuthenticationType));
        Assert.Equal(409, (await fixture.Call("GetStoreLifecycleRequest", routeValue: close.RequestId, principal: foreign, query: query)).Status);
        Assert.Equal(409, (await fixture.Call("RetireStoreLifecycleRequest", close with { CashAmount = 1 }, principal: principal)).Status);
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM store_lifecycle_retirements"));
        Assert.Equal("2", await fixture.Scalar("SELECT count(*) FROM store_lifecycle_commands"));
    }

    [Fact]
    public async Task Request_status_parses_large_exponent_cash_and_rejects_invalid_target()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 1000000000000000000000m, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", request, principal: LifecyclePrincipal())).Status);
        var query = RecoveryQuery(request).Replace("cashAmount=1000000000000000000000", "cashAmount=1e%2B21");
        var read = await fixture.Call("GetStoreLifecycleRequest", routeValue: request.RequestId, principal: LifecyclePrincipal(), query: query);
        Assert.Equal(200, read.Status);
        Assert.Equal("Completed", read.Body.GetProperty("status").GetString());
        Assert.Equal(400, (await fixture.Call("GetStoreLifecycleRequest", routeValue: request.RequestId,
            principal: LifecyclePrincipal(), query: query.Replace("1e%2B21", "NaN"))).Status);
    }
}
