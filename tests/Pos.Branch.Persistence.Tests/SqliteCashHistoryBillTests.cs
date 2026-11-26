using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteCashHistoryBillTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cash-bill-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 3, 0, 0, TimeSpan.Zero);
    private static JsonObject Bill(string request = "cash", string branch = "branch", string table = "A01", string batch = "ORDER-1") => JsonNode.Parse($$"""
        {"branchId":"{{branch}}","requestId":"payment-{{request}}","tableSessionId":"session-{{request}}","tableId":"{{table}}",
         "capturedAt":"2026-11-26T02:59:00Z","masterDataVersion":"original-master","calculationPolicyVersion":"original-policy",
         "vatRate":7,"serviceChargeRate":5.619,
         "items":[{"orderLineId":"line-1","orderBatchId":"{{batch}}","sellableItemId":"food","displayName":"Original food",
          "quantity":2,"unitPrice":50,"fulfillmentType":"Takeaway","comment":"Keep\noriginal\tcomment",
          "options":[{"optionId":"extra","displayName":"Original topping","quantityPerItem":1,"unitPrice":10}],
          "grossAmount":120,"discount":{"kind":"Percentage","requestedValue":8.333,"requestedAmount":10,"appliedAmount":10},"netAmount":110}],
         "itemGrossAmount":120,"lineDiscountAmount":10,"itemNetAmount":110,
         "billDiscount":{"kind":"FixedAmount","requestedValue":5,"requestedAmount":5,"appliedAmount":5},
         "serviceChargeBase":105,"serviceChargeAmount":5.9,"totalBeforeRounding":110.9,"roundingAdjustment":-0.9,
         "netTotal":110,"vatInclusiveAmount":7.26,"amountBeforeVat":103.64}
        """)!.AsObject();

    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync(); return store;
    }
    private async Task Sql(string text, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = text;
        foreach (var parameter in parameters) { command.Parameters.AddWithValue(parameter.Name, parameter.Value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task Seed(string request = "cash", string branch = "branch", string? json = null, decimal amount = 110)
    {
        await Store();
        var sale = new CashSaleResult("session-" + request, "employee", "device", 2, "shift", "store", "2026-11-26",
            amount + 10, amount, 10, "TEST-" + request, "QueuedForSimulation");
        await Sql("""
            INSERT INTO table_sessions VALUES($branch,$session,'CURRENT-TABLE','Completed',4);
            INSERT INTO cash_sales VALUES($branch,$request,$session,$sale,$bill);
            INSERT INTO simulated_cash_receipt_jobs VALUES($branch,$request,$receipt,$now);
            """, ("$branch", branch), ("$request", request), ("$session", sale.TableSessionId),
            ("$sale", JsonSerializer.Serialize(sale)), ("$bill", json ?? Bill(request, branch).ToJsonString()),
            ("$receipt", sale.ReceiptNumber), ("$now", Now.ToString("O", CultureInfo.InvariantCulture)));
    }
    private async Task<string> Fingerprint()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); var result = new StringBuilder();
        foreach (var table in new[] { "cash_sales", "simulated_cash_receipt_jobs", "table_sessions", "table_session_billing_policy", "order_batches", "order_lines", "order_line_options", "table_session_audit", "cloud_sync_outbox" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); result.Append(table).Append(JsonSerializer.Serialize(values)); }
        }
        return result.ToString();
    }

    [Fact]
    public async Task Original_items_options_discounts_and_table_survive_current_state_changes_without_read_mutations()
    {
        await Seed();
        await Sql("""
            INSERT INTO table_session_billing_policy(branch_id,table_session_id,master_data_version,calculation_policy_version,
                vat_rate,service_charge_rate,currency_scale,midpoint_rounding,created_at)
                VALUES('branch','session-cash','current-master','current-policy','7','5.619',2,'AwayFromZero',$now);
            INSERT INTO order_batches(branch_id,order_batch_id,request_id,table_session_id,device_id,employee_id,submitted_at)
                VALUES('branch','ORDER-1','order-request','session-cash','device','employee',$now);
            INSERT INTO order_lines(branch_id,order_line_id,order_batch_id,table_session_id,line_sequence,sellable_item_id,
                display_name,original_quantity,active_quantity,unit_price,fulfillment_type,status,created_at)
                VALUES('branch','line-1','ORDER-1','session-cash',1,'food','Current food',2,2,'50','Takeaway','Active',$now);
            INSERT INTO order_line_options(branch_id,order_line_id,option_sequence,option_id,display_name,quantity_per_item,unit_price)
                VALUES('branch','line-1',1,'extra','Current topping',1,'10');
            """, ("$now", Now.ToString("O", CultureInfo.InvariantCulture)));
        var store = await Store(); var before = await Fingerprint();
        var page = await store.ReadCashHistoryPageAsync("branch", Now, new(TableId: "A01", OrderBatchId: "ORDER-1"));
        var item = Assert.Single(page.Items); Assert.Equal("A01", item.TableId); Assert.Equal(new[] { "ORDER-1" }, item.OrderBatchIds);
        var detail = (await store.ReadCashHistoryDetailAsync("branch", "cash", Now))!;
        Assert.Equal("payment-cash", detail.Bill!.PaymentRequestId); Assert.NotEqual(detail.Transaction.RequestId, detail.Bill.PaymentRequestId);
        var line = Assert.Single(detail.Bill.Items); Assert.Equal("Original food", line.DisplayName);
        Assert.Equal("Original topping", Assert.Single(line.Options).DisplayName); Assert.Equal("Takeaway", line.FulfillmentType);
        Assert.Equal("Keep\noriginal\tcomment", line.Comment); Assert.Equal(10, line.Discount!.AppliedAmount);
        Assert.Equal(5, detail.Bill.BillDiscount!.AppliedAmount); Assert.Equal(110, detail.Financials!.AmountDue);
        Assert.Equal(before, await Fingerprint());
        await Sql("""
            UPDATE table_sessions SET table_id='RENAMED-TABLE',version=99;
            UPDATE table_session_billing_policy SET master_data_version='new-master',vat_rate='20';
            UPDATE order_lines SET display_name='Changed food',unit_price='999',active_quantity=1;
            UPDATE order_line_options SET display_name='Changed topping',unit_price='999';
            """);
        var reopened = new SqliteTableSessionCommandStore(_path);
        var later = (await reopened.ReadCashHistoryDetailAsync("branch", "cash", Now.AddDays(1)))!;
        Assert.Equal(JsonSerializer.Serialize(detail.Bill), JsonSerializer.Serialize(later.Bill));
        Assert.Equal(detail.Financials, later.Financials);
        Assert.Empty((await reopened.ReadCashHistoryPageAsync("other", Now, new(TableId: "A01"))).Items);
        Assert.Null(await reopened.ReadCashHistoryDetailAsync("other", "cash", Now));
    }

    [Fact]
    public async Task Legacy_thin_snapshot_remains_unknown_and_explicit_empty_zero_snapshot_is_known_empty()
    {
        await Seed("legacy", json: "{}", amount: 0);
        var empty = Bill("empty"); empty["items"] = new JsonArray(); empty.Remove("billDiscount");
        foreach (var key in new[] { "itemGrossAmount", "lineDiscountAmount", "itemNetAmount", "serviceChargeBase", "serviceChargeAmount", "totalBeforeRounding", "roundingAdjustment", "netTotal", "vatInclusiveAmount", "amountBeforeVat" }) { empty[key] = 0; }
        await Seed("empty", json: empty.ToJsonString(), amount: 0); var store = await Store();
        var legacy = (await store.ReadCashHistoryDetailAsync("branch", "legacy", Now))!;
        Assert.Null(legacy.Bill); Assert.Null(legacy.Transaction.TableId); Assert.Null(legacy.Transaction.OrderBatchIds); Assert.Null(legacy.Financials);
        var capturedEmpty = (await store.ReadCashHistoryDetailAsync("branch", "empty", Now))!;
        Assert.Empty(capturedEmpty.Bill!.Items); Assert.Empty(capturedEmpty.Transaction.OrderBatchIds!);
        Assert.Equal("A01", capturedEmpty.Transaction.TableId); Assert.Equal(0, capturedEmpty.Financials!.AmountDue);
        Assert.Empty((await store.ReadCashHistoryPageAsync("branch", Now, new(OrderBatchId: "ORDER-1"))).Items);
    }

    [Fact]
    public async Task Recorded_rounding_is_preserved_instead_of_recalculating_unit_prices_or_fixed_discounts()
    {
        var bill = Bill(); var line = bill["items"]![0]!.AsObject();
        line["quantity"] = 1; line["unitPrice"] = 1.005m; line["grossAmount"] = 1.01m; line["netAmount"] = 0;
        line["options"] = new JsonArray(); line["displayName"] = "Original\nname\ttext";
        line["discount"] = JsonNode.Parse("{\"kind\":\"FixedAmount\",\"requestedValue\":1.005,\"requestedAmount\":1.01,\"appliedAmount\":1.01}");
        bill.Remove("billDiscount"); bill["itemGrossAmount"] = 1.01m; bill["lineDiscountAmount"] = 1.01m;
        foreach (var key in new[] { "itemNetAmount", "serviceChargeBase", "serviceChargeAmount", "totalBeforeRounding", "roundingAdjustment", "netTotal", "vatInclusiveAmount", "amountBeforeVat" }) { bill[key] = 0; }
        await Seed(json: bill.ToJsonString(), amount: 0); var store = await Store();
        var saved = Assert.Single((await store.ReadCashHistoryDetailAsync("branch", "cash", Now))!.Bill!.Items);
        Assert.Equal(1.005m, saved.UnitPrice); Assert.Equal(1.01m, saved.GrossAmount);
        Assert.Equal(1.005m, saved.Discount!.RequestedValue); Assert.Equal(1.01m, saved.Discount.RequestedAmount);
        Assert.Equal("Original\nname\ttext", saved.DisplayName);
    }

    [Fact]
    public async Task Omitted_optional_fields_and_repeated_option_ids_are_preserved()
    {
        var bill = Bill(); var line = bill["items"]![0]!.AsObject(); line.Remove("comment");
        var repeated = line["options"]![0]!.DeepClone(); repeated["unitPrice"] = 0;
        line["options"]!.AsArray().Add(repeated);
        await Seed(json: bill.ToJsonString()); var store = await Store();
        var saved = Assert.Single((await store.ReadCashHistoryDetailAsync("branch", "cash", Now))!.Bill!.Items);
        Assert.Null(saved.Comment); Assert.Equal(2, saved.Options.Count);
    }

    [Fact]
    public async Task Omitted_null_discounts_are_not_required_or_fabricated()
    {
        var bill = Bill(); var line = bill["items"]![0]!.AsObject(); line.Remove("discount"); line.Remove("comment");
        line["netAmount"] = 120; bill.Remove("billDiscount"); bill["lineDiscountAmount"] = 0; bill["itemNetAmount"] = 120;
        bill["serviceChargeBase"] = 120; bill["serviceChargeAmount"] = 0; bill["totalBeforeRounding"] = 120;
        bill["roundingAdjustment"] = 0; bill["netTotal"] = 120; bill["vatInclusiveAmount"] = 0;
        bill["amountBeforeVat"] = 120; bill["vatRate"] = 0; bill["serviceChargeRate"] = 0;
        await Seed(json: bill.ToJsonString(), amount: 120); var store = await Store();
        var saved = (await store.ReadCashHistoryDetailAsync("branch", "cash", Now))!;
        Assert.Null(saved.Bill!.BillDiscount); Assert.Null(Assert.Single(saved.Bill.Items).Discount);
        Assert.Equal(0, saved.Financials!.LineDiscountAmount); Assert.Equal(0, saved.Financials.BillDiscountAmount);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("line")]
    [InlineData("option")]
    [InlineData("discount")]
    [InlineData("surrogate")]
    public async Task Ambiguous_or_malformed_JSON_text_is_not_projected_as_an_original_item(string corruption)
    {
        var json = Bill().ToJsonString();
        json = corruption switch
        {
            "root" => json.Replace("\"branchId\":\"branch\"", "\"branchId\":\"branch\",\"branchId\":\"branch\"", StringComparison.Ordinal),
            "line" => json.Replace("\"quantity\":2", "\"quantity\":2,\"quantity\":2", StringComparison.Ordinal),
            "option" => json.Replace("\"quantityPerItem\":1", "\"quantityPerItem\":1,\"quantityPerItem\":1", StringComparison.Ordinal),
            "discount" => json.Replace("\"requestedAmount\":10", "\"requestedAmount\":10,\"requestedAmount\":10", StringComparison.Ordinal),
            _ => json.Replace("Original food", "\\uD800", StringComparison.Ordinal)
        };
        await Seed(json: json); var store = await Store();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryDetailAsync("branch", "cash", Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryPageAsync("branch", Now, new()));
    }

    [Fact]
    public async Task Table_and_batch_filters_are_bound_to_cursor_scope_and_old_version_requires_refresh()
    {
        await Seed("one"); await Seed("two"); var store = await Store();
        var query = new CashHistoryQuery(TableId: "A01", OrderBatchId: "ORDER-1", Limit: 1);
        var first = await store.ReadCashHistoryPageAsync("branch", Now, query); Assert.NotNull(first.NextCursor);
        await Seed("three");
        var second = await store.ReadCashHistoryPageAsync("branch", Now, query with { Cursor = first.NextCursor });
        Assert.Equal("one", Assert.Single(second.Items).RequestId); Assert.Null(second.NextCursor);
        foreach (var changed in new[] { query with { TableId = null }, query with { OrderBatchId = null }, query with { TableId = "other" }, query with { OrderBatchId = "other" } })
        { await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPageAsync("branch", Now, changed with { Cursor = first.NextCursor })); }
        var decoded = JsonNode.Parse(Convert.FromBase64String(first.NextCursor!.Replace('-', '+').Replace('_', '/').PadRight((first.NextCursor.Length + 3) / 4 * 4, '=')))!.AsObject();
        Assert.Equal(2, decoded["Version"]!.GetValue<int>()); decoded["Version"] = 1;
        var old = Convert.ToBase64String(Encoding.UTF8.GetBytes(decoded.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPageAsync("branch", Now, query with { Cursor = old }));
        Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now, query)).Items);
        Assert.Empty((await store.ReadCashHistoryPageAsync("branch", Now, new(TableId: "CURRENT-TABLE"))).Items);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("session")]
    [InlineData("duplicate-line")]
    [InlineData("quantity")]
    [InlineData("options")]
    [InlineData("option-quantity")]
    [InlineData("gross")]
    [InlineData("aggregate")]
    [InlineData("percentage")]
    [InlineData("discount")]
    [InlineData("fulfillment")]
    [InlineData("items-type")]
    [InlineData("oversize")]
    public async Task Malformed_present_item_capture_is_unavailable_instead_of_becoming_legacy_unknown(string corruption)
    {
        var bill = Bill(); var line = bill["items"]![0]!.AsObject();
        switch (corruption)
        {
            case "branch": bill["branchId"] = "other"; break;
            case "session": bill["tableSessionId"] = "another-session"; break;
            case "duplicate-line": bill["items"]!.AsArray().Add(line.DeepClone()); break;
            case "quantity": line["quantity"] = 0; break;
            case "options": line["options"] = new JsonObject(); break;
            case "option-quantity": line["options"]![0]!["quantityPerItem"] = 0; break;
            case "gross": line["grossAmount"] = 121; break;
            case "aggregate": line["grossAmount"] = 121; line["netAmount"] = 111; break;
            case "percentage": line["discount"]!["requestedValue"] = 101; break;
            case "discount": line["discount"]!["appliedAmount"] = 11; break;
            case "fulfillment": line["fulfillmentType"] = "private-invalid"; break;
            case "items-type": bill["items"] = new JsonObject(); break;
            default: bill["padding"] = new string('x', 1024 * 1024); break;
        }
        await Seed(json: bill.ToJsonString()); var store = await Store(); var before = await Fingerprint();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryDetailAsync("branch", "cash", Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryPageAsync("branch", Now, new()));
        Assert.Equal(before, await Fingerprint());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
