using System.Text.Json;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Theory]
    [InlineData(220, 0, 720, 0)]
    [InlineData(300, 80, 730, 10)]
    public async Task Concurrent_cash_submissions_settle_once_and_reconcile_from_persisted_sales(
        int tender, int change, int countedCash, int difference)
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashier = Principal();
        cashier.AddIdentity(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(BranchPosClaimTypes.DeviceRole, "PcPos")],
            BranchEmployeeSessionAuthenticationDefaults.SchemeName));
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        Assert.True(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-1", "STORE-1", new DateOnly(2026, 10, 31), 500m, default));
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
        var version = payment.Body.GetProperty("result").GetProperty("tableVersion").GetInt64();

        Task<(int Status, JsonElement Body, string CacheControl)> Complete(string id, decimal amount) =>
            fixture.Call("CompleteBranchCashSale", new
            {
                schemaVersion = 1,
                context = Context(id, "SESSION-1"),
                expectedVersion = version,
                tenderedAmount = amount
            }, principal: cashier);

        Assert.Equal(409, (await Complete("SHORT", 219.99m)).Status);
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM cash_sales;"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submissions = new[] { "CASH-A", "CASH-B" }.Select(id => Task.Run(async () =>
        {
            await start.Task;
            return (Id: id, Result: await Complete(id, tender));
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(submissions);
        var winner = Assert.Single(results, entry => entry.Result.Status == 200);
        Assert.Single(results, entry => entry.Result.Status == 409);
        Assert.Equal((decimal)change, winner.Result.Body.GetProperty("changeAmount").GetDecimal());
        Assert.Equal(winner.Result.Body.GetRawText(), (await Complete(winner.Id, tender)).Body.GetRawText());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cash_sales;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cloud_sync_outbox WHERE action = 'CompletePayment';"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM simulated_cash_receipt_jobs;"));
        Assert.Equal("Completed", await fixture.Scalar("SELECT status FROM table_sessions;"));

        var reopenedStore = new SqliteTableSessionCommandStore(fixture.Path);
        var close = await reopenedStore.CloseTestCashCycleAsync("BRANCH-01", "SHIFT-1",
            countedCash, default, "EMP-1", DateTimeOffset.UnixEpoch);
        Assert.Equal(new TestCashClose(720m, countedCash, difference, "EMP-1", DateTimeOffset.UnixEpoch), close);
    }
}
