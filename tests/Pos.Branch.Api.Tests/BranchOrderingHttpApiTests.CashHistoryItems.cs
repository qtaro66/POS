using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private static async Task CapturedItemHistory(Fixture fixture)
    {
        Assert.Equal(200, (await fixture.Call("OpenBranchTable", Open("OPEN-ITEMS", "SESSION-1", "A01"))).Status);
        Assert.Equal(200, (await fixture.Call("SubmitBranchOrder", Submit("ORDER-ITEMS"))).Status);
        await fixture.Execute("""
            UPDATE order_lines SET discount_kind='Percentage',discount_requested_value='10' WHERE fulfillment_type='DineIn';
            UPDATE table_session_billing_policy SET bill_discount_kind='FixedAmount',bill_discount_requested_value='5',service_charge_rate='5';
            """);
        var session = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1");
        var payment = await fixture.Call("RequestBranchTablePayment", new
        {
            schemaVersion = 1,
            context = Context("PAY-ITEMS", "SESSION-1"),
            expectedVersion = session.Body.GetProperty("session").GetProperty("tableVersion").GetInt64()
        });
        Assert.Equal(200, payment.Status);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        Assert.True(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-ITEMS", "STORE-ITEMS",
            DateOnly.ParseExact(CashHistoryDay(), "yyyy-MM-dd", CultureInfo.InvariantCulture), 500, default));
        var completed = await fixture.Call("CompleteBranchCashSale", new
        {
            schemaVersion = 1,
            context = Context("CASH-ITEMS", "SESSION-1"),
            expectedVersion = payment.Body.GetProperty("result").GetProperty("tableVersion").GetInt64(),
            tenderedAmount = 300m
        }, principal: LifecyclePrincipal());
        Assert.Equal(200, completed.Status);
    }

    [Fact]
    public async Task Cash_history_items_use_the_original_payment_snapshot_after_current_data_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        await CapturedItemHistory(fixture);
        var before = await CashHistoryFingerprint(fixture);
        var query = "?tableId=A01&orderBatchId=" + Uri.EscapeDataString("BRANCH-01:ORDER-ITEMS:batch:1");
        var page = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(200, page.Status); Assert.Equal("no-store", page.CacheControl);
        var item = Assert.Single(page.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("A01", item.GetProperty("tableId").GetString());
        Assert.Equal("BRANCH-01:ORDER-ITEMS:batch:1", Assert.Single(item.GetProperty("orderBatchIds").EnumerateArray()).GetString());
        var detail = await fixture.Call("GetCashHistoryDetail", routeValue: "CASH-ITEMS", principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status); Assert.Equal("no-store", detail.CacheControl);
        Assert.Equal(item.GetRawText(), detail.Body.GetProperty("transaction").GetRawText());
        var bill = detail.Body.GetProperty("bill");
        Assert.Equal(1, bill.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("BRANCH-01", bill.GetProperty("branchId").GetString());
        Assert.Equal("SESSION-1", bill.GetProperty("tableSessionId").GetString());
        Assert.Equal("A01", bill.GetProperty("tableId").GetString());
        Assert.Equal("PAY-ITEMS", bill.GetProperty("paymentRequestId").GetString());
        Assert.NotEqual("CASH-ITEMS", bill.GetProperty("paymentRequestId").GetString());
        Assert.Equal("OLD", bill.GetProperty("masterDataVersion").GetString());
        Assert.Equal("CALC-1", bill.GetProperty("calculationPolicyVersion").GetString());
        Assert.Equal(7, bill.GetProperty("vatRate").GetDecimal());
        Assert.Equal(5, bill.GetProperty("serviceChargeRate").GetDecimal());
        var lines = bill.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, lines.Length);
        var dineIn = Assert.Single(lines, line => line.GetProperty("fulfillmentType").GetString() == "DineIn");
        var takeaway = Assert.Single(lines, line => line.GetProperty("fulfillmentType").GetString() == "Takeaway");
        Assert.All(lines, line =>
        {
            Assert.Equal("Test item", line.GetProperty("displayName").GetString());
            Assert.Equal("ITEM-1", line.GetProperty("sellableItemId").GetString());
            Assert.Equal(1, line.GetProperty("quantity").GetInt32());
            Assert.Equal(100, line.GetProperty("unitPrice").GetDecimal());
            Assert.Equal(110, line.GetProperty("grossAmount").GetDecimal());
            Assert.Equal("test", line.GetProperty("comment").GetString());
            var option = Assert.Single(line.GetProperty("options").EnumerateArray());
            Assert.Equal("OPT-1", option.GetProperty("optionId").GetString());
            Assert.Equal("Test option", option.GetProperty("displayName").GetString());
            Assert.Equal(1, option.GetProperty("quantityPerItem").GetInt32());
            Assert.Equal(10, option.GetProperty("unitPrice").GetDecimal());
        });
        Assert.Equal("Percentage", dineIn.GetProperty("discount").GetProperty("kind").GetString());
        Assert.Equal(10, dineIn.GetProperty("discount").GetProperty("requestedValue").GetDecimal());
        Assert.Equal(11, dineIn.GetProperty("discount").GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(99, dineIn.GetProperty("netAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, takeaway.GetProperty("discount").ValueKind);
        Assert.Equal("FixedAmount", bill.GetProperty("billDiscount").GetProperty("kind").GetString());
        Assert.Equal(5, bill.GetProperty("billDiscount").GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(before, await CashHistoryFingerprint(fixture));

        await fixture.Execute("""
            UPDATE master_data_sellable_items SET display_name='CURRENT-MASTER',resolved_price='999';
            UPDATE order_lines SET display_name='CURRENT-LINE',unit_price='777',comment='CURRENT-COMMENT';
            UPDATE order_line_options SET display_name='CURRENT-OPTION',unit_price='888';
            UPDATE table_sessions SET table_id='CURRENT-TABLE';
            UPDATE table_session_billing_policy SET vat_rate='99',service_charge_rate='99';
            """);
        var refreshed = await fixture.Call("GetCashHistoryDetail", routeValue: "CASH-ITEMS", principal: LifecyclePrincipal());
        Assert.Equal(200, refreshed.Status);
        Assert.Equal(detail.Body.GetProperty("transaction").GetRawText(), refreshed.Body.GetProperty("transaction").GetRawText());
        Assert.Equal(bill.GetRawText(), refreshed.Body.GetProperty("bill").GetRawText());
        Assert.Equal(detail.Body.GetProperty("financials").GetRawText(), refreshed.Body.GetProperty("financials").GetRawText());
        Assert.DoesNotContain("CURRENT-", refreshed.Body.GetRawText());
        Assert.Equal(before, await CashHistoryFingerprint(fixture));
        Assert.Single((await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: query)).Body.GetProperty("items").EnumerateArray());
        Assert.Empty((await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?tableId=CURRENT-TABLE")).Body.GetProperty("items").EnumerateArray());
        Assert.Equal(404, (await fixture.Call("GetCashHistoryDetail", routeValue: "CASH-ITEMS", principal: LifecyclePrincipal(branch: "OTHER"))).Status);
        Assert.Empty((await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(branch: "OTHER"), query: query)).Body.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("?tableId=")]
    [InlineData("?tableId=%20A01")]
    [InlineData("?tableId=A01%20")]
    [InlineData("?tableId=%0A")]
    [InlineData("?tableId=%C2%85")]
    [InlineData("?tableId=A01&tableId=A02")]
    [InlineData("?TableId=A01")]
    [InlineData("?orderBatchId=")]
    [InlineData("?orderBatchId=%20ORDER-1")]
    [InlineData("?orderBatchId=%09")]
    [InlineData("?orderBatchId=ONE&orderBatchId=TWO")]
    [InlineData("?OrderBatchId=ONE")]
    public async Task Cash_history_items_reject_invalid_or_ambiguous_snapshot_filters(string query)
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal("InvalidCashHistoryQuery", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Cash_history_items_preserve_legacy_absence_and_bind_new_filters_in_cursor_version_two()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await SavedCashHistory(fixture);
        await SavedCashHistory(fixture);
        var page = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?limit=1");
        Assert.Equal(200, page.Status);
        var row = Assert.Single(page.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("tableId").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("orderBatchIds").ValueKind);
        var detail = await fixture.Call("GetCashHistoryDetail", routeValue: first.Request, principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status); Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("bill").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, detail.Body.GetProperty("financials").ValueKind);
        Assert.Empty((await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?tableId=ORIGINAL-TABLE")).Body.GetProperty("items").EnumerateArray());
        var cursor = page.Body.GetProperty("nextCursor").GetString()!;
        var decoded = JsonNode.Parse(Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/').PadRight((cursor.Length + 3) / 4 * 4, '=')))!;
        Assert.Equal(2, decoded["Version"]!.GetValue<int>());
        foreach (var filter in new[] { "tableId=A01", "orderBatchId=ORDER-1" })
        {
            var changed = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?limit=1&" + filter + "&cursor=" + cursor);
            Assert.Equal(400, changed.Status);
        }
        decoded["Version"] = 1; decoded.AsObject().Remove("TableId"); decoded.AsObject().Remove("OrderBatchId");
        var old = Convert.ToBase64String(Encoding.UTF8.GetBytes(decoded.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(400, (await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?limit=1&cursor=" + old)).Status);
        var next = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?limit=1&cursor=" + cursor);
        Assert.Equal(200, next.Status); Assert.Equal(first.Request, Assert.Single(next.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
    }

    [Theory]
    [InlineData("UPDATE cash_sales SET bill_snapshot_json=json_set(bill_snapshot_json,'$.branchId','PRIVATE-OTHER-BRANCH');")]
    [InlineData("UPDATE cash_sales SET bill_snapshot_json=json_set(bill_snapshot_json,'$.tableSessionId','PRIVATE-WRONG-SESSION');")]
    [InlineData("UPDATE cash_sales SET bill_snapshot_json=json_set(bill_snapshot_json,'$.items[0].orderBatchId',17);")]
    public async Task Cash_history_items_corrupt_modern_snapshot_is_unavailable_without_exposing_it(string corruption)
    {
        await using var fixture = await Fixture.CreateAsync(); await CapturedItemHistory(fixture);
        await fixture.Execute(corruption);
        var before = await CashHistoryFingerprint(fixture);
        var response = await fixture.Call("GetCashHistoryDetail", routeValue: "CASH-ITEMS", principal: LifecyclePrincipal());
        Assert.Equal(503, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(JsonValueKind.Undefined, response.Body.ValueKind);
        Assert.Equal(before, await CashHistoryFingerprint(fixture));
    }
}
