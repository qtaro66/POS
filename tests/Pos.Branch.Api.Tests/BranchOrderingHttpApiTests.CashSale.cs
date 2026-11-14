using System.Security.Claims;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Fact]
    public async Task Cash_sale_requires_an_open_shift_and_replays_without_a_second_payment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashier = Principal();
        cashier.AddIdentity(new ClaimsIdentity([new Claim(BranchPosClaimTypes.DeviceRole, "PcPos")],
            BranchEmployeeSessionAuthenticationDefaults.SchemeName));
        Assert.Equal(200, (await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"))).Status);
        Assert.Equal(200, (await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"))).Status);
        var detail = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1");
        var payment = await fixture.Call("RequestBranchTablePayment", new
        {
            schemaVersion = 1,
            context = Context("PAY-1", "SESSION-1"),
            expectedVersion = detail.Body.GetProperty("session").GetProperty("tableVersion").GetInt64()
        });
        Assert.Equal(200, payment.Status);
        var request = new
        {
            schemaVersion = 1,
            context = Context("CASH-1", "SESSION-1"),
            expectedVersion = payment.Body.GetProperty("result").GetProperty("tableVersion").GetInt64(),
            tenderedAmount = 300m
        };
        Assert.Equal(409, (await fixture.Call("CompleteBranchCashSale", request, principal: cashier)).Status);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        Assert.True(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-1", "STORE-1", new DateOnly(2026, 10, 31), default));
        Assert.False(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-2", "STORE-2", new DateOnly(2026, 10, 31), default));
        Assert.Equal(403, (await fixture.Call("CompleteBranchCashSale", request)).Status);
        var first = await fixture.Call("CompleteBranchCashSale", request, principal: cashier);
        Assert.Equal(200, first.Status);
        Assert.Equal(220m, first.Body.GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(80m, first.Body.GetProperty("changeAmount").GetDecimal());
        Assert.Equal("SHIFT-1", first.Body.GetProperty("shiftId").GetString());
        var replay = await fixture.Call("CompleteBranchCashSale", request, principal: cashier);
        Assert.Equal(first.Body.GetRawText(), replay.Body.GetRawText());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cash_sales;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM simulated_cash_receipt_jobs;"));
        Assert.Equal("QueuedForSimulation", replay.Body.GetProperty("printStatus").GetString());
        Assert.Equal(720m, await new SqliteTableSessionCommandStore(fixture.Path).ReadExpectedTestCashAsync("BRANCH-01", 500m, default));
        Assert.Equal("Completed", await fixture.Scalar("SELECT status FROM table_sessions;"));
        Assert.Null(await store.CloseTestCashCycleAsync("BRANCH-01", "OTHER", 500m, 710m, default));
        Assert.Equal(new TestCashClose(720m, 710m, -10m),
            await store.CloseTestCashCycleAsync("BRANCH-01", "SHIFT-1", 500m, 710m, default));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM test_cash_close_history;"));
        Assert.Null(await store.ReadExpectedTestCashAsync("BRANCH-01", 500m, default));
    }
}
