using System.Text.Json;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private static async Task<object> PrepareLifecycleCash(Fixture fixture, string suffix, string tableId)
    {
        var sessionId = "SESSION-" + suffix;
        Assert.Equal(200, (await fixture.Call("OpenBranchTable", Open("OPEN-" + suffix, sessionId, tableId))).Status);
        var order = JsonSerializer.SerializeToNode(Submit("ORDER-" + suffix))!;
        order["context"]!["tableSessionId"] = sessionId;
        Assert.Equal(200, (await fixture.Call("SubmitBranchOrder", order)).Status);
        var detail = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: sessionId);
        var payment = await fixture.Call("RequestBranchTablePayment", new
        {
            schemaVersion = 1,
            context = Context("PAY-" + suffix, sessionId),
            expectedVersion = detail.Body.GetProperty("session").GetProperty("tableVersion").GetInt64()
        });
        Assert.Equal(200, payment.Status);
        return new
        {
            schemaVersion = 1,
            context = Context("CASH-" + suffix, sessionId),
            expectedVersion = payment.Body.GetProperty("result").GetProperty("tableVersion").GetInt64(),
            tenderedAmount = 300m
        };
    }

    [Fact]
    public async Task Cash_settlement_tracks_durable_shifts_and_freezes_closing_totals_across_handover()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = LifecyclePrincipal();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 500, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: principal)).Status);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        Assert.True(await store.OpenTestCashCycleAsync("BRANCH-01", "LEGACY", "LEGACY",
            new DateOnly(2026, 10, 31), 999, default));
        var firstRequest = await PrepareLifecycleCash(fixture, "FIRST", "A01");
        var first = await fixture.Call("CompleteBranchCashSale", firstRequest, principal: principal);
        Assert.Equal(200, first.Status);
        Assert.Equal(open.ShiftId, first.Body.GetProperty("shiftId").GetString());
        Assert.Equal(open.StoreSessionId, first.Body.GetProperty("storeSessionId").GetString());
        Assert.Equal(220, first.Body.GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(80, first.Body.GetProperty("changeAmount").GetDecimal());

        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseShift", CashAmount = null };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: principal)).Status);
        var firstClosing = await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: open.ShiftId, principal: principal);
        Assert.Equal(720, firstClosing.Body.GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(220, firstClosing.Body.GetProperty("cashSummary").GetProperty("shift").GetProperty("appliedCashAmount").GetDecimal());

        var secondRequest = await PrepareLifecycleCash(fixture, "SECOND", "A02");
        Assert.Equal(409, (await fixture.Call("CompleteBranchCashSale", secondRequest, principal: principal)).Status);
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM cash_sales"));
        var next = open with
        {
            RequestId = Guid.NewGuid().ToString(),
            ShiftId = Guid.NewGuid().ToString(),
            Operation = "OpenShift",
            CashAmount = 999
        };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", next, principal: principal)).Status);
        var second = await fixture.Call("CompleteBranchCashSale", secondRequest, principal: principal);
        Assert.Equal(200, second.Status);
        Assert.Equal(next.ShiftId, second.Body.GetProperty("shiftId").GetString());
        Assert.Equal(open.StoreSessionId, second.Body.GetProperty("storeSessionId").GetString());

        var end = next with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 940 };
        var ended = await fixture.Call("ExecuteStoreLifecycle", end, principal: principal);
        Assert.Equal(200, ended.Status);
        Assert.Equal(940, ended.Body.GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(0, ended.Body.GetProperty("closing").GetProperty("cashDifference").GetDecimal());
        Assert.Equal(second.Body.GetRawText(),
            (await fixture.Call("CompleteBranchCashSale", secondRequest, principal: principal)).Body.GetRawText());
        Assert.Equal(firstClosing.Body.GetRawText(), (await fixture.Call("GetStoreShiftClosing",
            routeValue: open.StoreSessionId, shiftId: open.ShiftId, principal: principal)).Body.GetRawText());
        var endSnapshot = await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: next.ShiftId, principal: principal);
        Assert.Equal(2, endSnapshot.Body.GetProperty("cashSummary").GetProperty("store").GetProperty("receiptCount").GetInt32());
        Assert.Equal(220, endSnapshot.Body.GetProperty("cashSummary").GetProperty("shift").GetProperty("appliedCashAmount").GetDecimal());

        var closedRequest = await PrepareLifecycleCash(fixture, "CLOSED", "A01");
        Assert.Equal(409, (await fixture.Call("CompleteBranchCashSale", closedRequest, principal: principal)).Status);
        Assert.Equal("2", await fixture.Scalar("SELECT count(*) FROM cash_sales"));
        Assert.Equal("2", await fixture.Scalar("SELECT count(*) FROM simulated_cash_receipt_jobs"));
        Assert.Equal("2", await fixture.Scalar("SELECT count(*) FROM cloud_sync_outbox WHERE action='CompletePayment'"));
    }

    [Fact]
    public async Task Racing_store_close_and_cash_sale_produce_one_consistent_closing_snapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var principal = LifecyclePrincipal();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 500, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: principal)).Status);
        var cash = await PrepareLifecycleCash(fixture, "RACE", "A01");
        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 500 };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payment = Task.Run(async () => { await start.Task; return await fixture.Call("CompleteBranchCashSale", cash, principal: principal); });
        var closure = Task.Run(async () => { await start.Task; return await fixture.Call("ExecuteStoreLifecycle", close, principal: principal); });
        start.SetResult();
        await Task.WhenAll(payment, closure);
        var paymentResult = await payment;
        var closeResult = await closure;
        Assert.Equal(200, closeResult.Status);
        Assert.Contains(paymentResult.Status, new[] { 200, 409 });
        var committed = paymentResult.Status == 200;
        Assert.Equal(committed ? 720 : 500,
            closeResult.Body.GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
        var snapshot = await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: open.ShiftId, principal: principal);
        Assert.Equal(committed ? 1 : 0, snapshot.Body.GetProperty("cashSummary").GetProperty("store").GetProperty("receiptCount").GetInt32());
        Assert.Equal(committed ? "1" : "0", await fixture.Scalar("SELECT count(*) FROM cash_sales"));
        Assert.Equal(snapshot.Body.GetProperty("cashSummary").GetProperty("store").GetProperty("receiptCount").GetInt32().ToString(),
            await fixture.Scalar("SELECT count(*) FROM simulated_cash_receipt_jobs"));
    }
    [Fact]
    public async Task Cash_sale_rejects_a_prior_business_date_without_mutating_the_pending_bill()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        await store.ExecuteStoreLifecycleAsync("BRANCH-01", new("prior", "prior-store", "prior-shift",
            StoreLifecycleAction.OpenStore, 500, new Pos.Domain.StoreSessions.StoreOperationContext("EMP-2", "DEV-1", now, date)));
        var cash = await PrepareLifecycleCash(fixture, "DATE", "A01");
        Assert.Equal(409, (await fixture.Call("CompleteBranchCashSale", cash, principal: LifecyclePrincipal())).Status);
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM cash_sales"));
        Assert.Equal("PaymentPending", await fixture.Scalar("SELECT status FROM table_sessions"));
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM simulated_cash_receipt_jobs"));
    }
    [Fact]
    public async Task Closing_financials_use_the_settled_bill_rounding_and_tax_snapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Execute("UPDATE master_data_sellable_items SET resolved_price='40.45' WHERE master_data_version='OLD' AND sellable_item_id='ITEM-1';");
        var principal = LifecyclePrincipal();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 0, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: principal)).Status);
        var request = JsonSerializer.SerializeToNode(await PrepareLifecycleCash(fixture, "ROUND", "A01"))!;
        request["tenderedAmount"] = 100;
        var sale = await fixture.Call("CompleteBranchCashSale", request, principal: principal);
        Assert.Equal(200, sale.Status);
        Assert.Equal(100, sale.Body.GetProperty("appliedAmount").GetDecimal());
        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 100 };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: principal)).Status);
        var saved = await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: open.ShiftId, principal: principal);
        var totals = saved.Body.GetProperty("cashSummary").GetProperty("store").GetProperty("financials");
        Assert.Equal(100.9m, totals.GetProperty("totalBeforeRounding").GetDecimal());
        Assert.Equal(-0.9m, totals.GetProperty("roundingAdjustment").GetDecimal());
        Assert.Equal(100, totals.GetProperty("amountDue").GetDecimal());
        Assert.Equal(6.6m, totals.GetProperty("vatInclusiveAmount").GetDecimal());
        await fixture.Execute("UPDATE master_data_billing_policy SET vat_rate='0';");
        Assert.Equal(saved.Body.GetRawText(), (await fixture.Call("GetStoreShiftClosing", routeValue: open.StoreSessionId,
            shiftId: open.ShiftId, principal: principal)).Body.GetRawText());
    }
}
