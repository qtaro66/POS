using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Xunit.Abstractions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteCashHistoryQueryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cash-history-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 3, 0, 0, TimeSpan.Zero);
    private const string Bill = """
        {"itemGrossAmount":120,"lineDiscountAmount":10,"billDiscount":{"appliedAmount":5},"serviceChargeAmount":5.9,"totalBeforeRounding":110.9,"roundingAdjustment":-0.9,"netTotal":110,"vatInclusiveAmount":7.26,"amountBeforeVat":103.64}
        """;
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync(); return store;
    }
    private async Task Execute(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var parameter in parameters) { command.Parameters.AddWithValue(parameter.Name, parameter.Value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task Seed(string request, string branch = "branch", string day = "2026-11-24", string? receipt = null,
        decimal applied = 110, string? bill = null)
    {
        await Store(); receipt ??= "TEST-" + request;
        var sale = new CashSaleResult("session-" + request, "original-employee", "original-device", 2,
            "original-shift", "original-store", day, applied + 10, applied, 10, receipt, "QueuedForSimulation");
        await Execute("""
            INSERT INTO table_sessions VALUES($branch,$session,'ORIGINAL-TABLE','Completed',4);
            INSERT INTO cash_sales VALUES($branch,$request,$session,$result,$bill);
            INSERT INTO simulated_cash_receipt_jobs VALUES($branch,$request,$receipt,$now);
            """, ("$branch", branch), ("$request", request), ("$session", sale.TableSessionId),
            ("$result", JsonSerializer.Serialize(sale)), ("$bill", bill ?? Bill), ("$receipt", receipt),
            ("$now", Now.ToString("O", CultureInfo.InvariantCulture)));
    }
    private async Task<string> Fingerprint()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); var results = new List<string>();
        foreach (var table in new[] { "cash_sales", "simulated_cash_receipt_jobs", "table_sessions", "table_session_audit", "cloud_sync_outbox" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount]; reader.GetValues(values); results.Add(table + JsonSerializer.Serialize(values));
            }
        }
        return string.Join("\n", results);
    }
    private async Task<long> SchemaCookie()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA schema_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Bundled_SQLite_VACUUM_changes_the_schema_cookie()
    {
        await Seed("one"); var before = await SchemaCookie(); await Execute("VACUUM;"); var after = await SchemaCookie();
        output.WriteLine($"Bundled SQLite schema_version before VACUUM: {before}; after VACUUM: {after}");
        Assert.Equal(before + 1, after);
    }

    [Theory]
    [InlineData("VACUUM;")]
    [InlineData("CREATE INDEX synthetic_cash_history ON cash_sales(request_id);")]
    public async Task Database_maintenance_requires_a_fresh_first_page_instead_of_reusing_old_row_positions(string maintenance)
    {
        await Seed("one"); await Seed("two"); var store = await Store(); var query = new CashHistoryQuery(Limit: 1);
        var page = await store.ReadCashHistoryPageAsync("branch", Now, query); Assert.NotNull(page.NextCursor);
        await Execute(maintenance); var before = await Fingerprint();
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPageAsync("branch", Now, query with { Cursor = page.NextCursor }));
        Assert.Equal(before, await Fingerprint());
        Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now, query)).Items);
    }

    [Fact]
    public async Task Captured_payment_and_financials_are_immutable_read_only_and_branch_scoped()
    {
        await Seed("one"); await Seed("one", "other"); var store = await Store();
        var before = await Fingerprint();
        var page = await store.ReadCashHistoryPageAsync("branch", Now, new());
        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.SchemaVersion); Assert.Equal("2026-11-24", page.BusinessDate);
        Assert.Equal("one", item.RequestId); Assert.Equal("Cash", item.PaymentMethod); Assert.Equal("Completed", item.Status);
        Assert.Equal(120, item.TenderedAmount); Assert.Equal(110, item.AppliedAmount); Assert.Equal(10, item.ChangeAmount);
        Assert.Equal(Now, item.CapturedAt); Assert.Equal("QueuedForSimulation", item.CapturedPrintStatus);
        var detail = Assert.IsType<CashHistoryDetail>(await store.ReadCashHistoryDetailAsync("branch", "one", Now));
        Assert.Equal(item, detail.Transaction); Assert.Equal(110, detail.Financials!.AmountDue);
        Assert.Equal(7.26m, detail.Financials.VatInclusiveAmount); Assert.Equal(-0.9m, detail.Financials.RoundingAdjustment);
        Assert.Null(await store.ReadCashHistoryDetailAsync("missing-branch", "one", Now));
        Assert.Empty((await store.ReadCashHistoryPageAsync("missing-branch", Now, new())).Items);
        Assert.Equal(before, await Fingerprint());
        await Execute("UPDATE table_sessions SET table_id='NEW-CURRENT-NAME',status='Ordering',version=99;");
        var reopened = new SqliteTableSessionCommandStore(_path);
        Assert.Equal(detail, await reopened.ReadCashHistoryDetailAsync("branch", "one", Now));
    }

    [Fact]
    public async Task Default_day_matches_Bangkok_calendar_and_does_not_reuse_an_old_open_store_day()
    {
        await Seed("today"); await Seed("old", day: "2026-11-23"); var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", new("open", "store", "shift", StoreLifecycleAction.OpenStore, 0,
            new("employee", "device", Now.AddDays(-1), new(2026, 11, 23))));
        Assert.Equal("today", Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now, new())).Items).RequestId);
        Assert.Equal("old", Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now, new(BusinessDate: "2026-11-23"))).Items).RequestId);
        var midnight = await store.ReadCashHistoryPageAsync("branch", new(2026, 11, 23, 18, 0, 0, TimeSpan.Zero), new());
        Assert.Equal("2026-11-24", midnight.BusinessDate);
    }

    [Fact]
    public async Task Literal_receipt_search_and_session_filters_do_not_treat_wildcards_as_patterns()
    {
        await Seed("one", receipt: "TEST-A_%"); await Seed("two", receipt: "TEST-B"); var store = await Store();
        Assert.Equal("one", Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now, new(ReceiptNumber: "%"))).Items).RequestId);
        Assert.Empty((await store.ReadCashHistoryPageAsync("branch", Now, new(ReceiptNumber: "test"))).Items);
        Assert.Equal("two", Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now,
            new(ReceiptNumber: "TEST", TableSessionId: "session-two", PaymentMethod: "Cash", Status: "Completed"))).Items).RequestId);
    }

    [Fact]
    public async Task Continuation_freezes_highwater_and_default_day_across_midnight_append_and_reopen()
    {
        await Seed("one"); await Seed("two"); var store = await Store();
        var query = new CashHistoryQuery(Limit: 1);
        var first = await store.ReadCashHistoryPageAsync("branch", Now, query);
        Assert.Equal("two", Assert.Single(first.Items).RequestId); Assert.NotNull(first.NextCursor);
        await Seed("late"); await Seed("tomorrow", day: "2026-11-25");
        var reopened = new SqliteTableSessionCommandStore(_path);
        var next = await reopened.ReadCashHistoryPageAsync("branch", Now.AddDays(1), query with { Cursor = first.NextCursor });
        Assert.Equal("one", Assert.Single(next.Items).RequestId); Assert.Equal(first.BusinessDate, next.BusinessDate); Assert.Null(next.NextCursor);
        Assert.Equal("tomorrow", Assert.Single((await reopened.ReadCashHistoryPageAsync("branch", Now.AddDays(1), new())).Items).RequestId);
        Assert.Equal("late", Assert.Single((await reopened.ReadCashHistoryPageAsync("branch", Now, query)).Items).RequestId);
    }

    [Fact]
    public async Task Cursor_binds_exact_filters_branch_and_limit_and_rejects_unknown_duplicate_or_missing_fields()
    {
        await Seed("one"); await Seed("two"); var store = await Store(); var query = new CashHistoryQuery(Limit: 1);
        var cursor = (await store.ReadCashHistoryPageAsync("branch", Now, query)).NextCursor!;
        foreach (var changed in new[] { query with { BusinessDate = "2026-11-24" }, query with { ReceiptNumber = "TEST" },
            query with { TableSessionId = "session-one" }, query with { PaymentMethod = "Cash" }, query with { Status = "Completed" }, query with { Limit = 2 } })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPageAsync("branch", Now, changed with { Cursor = cursor }));
        }
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPageAsync("other", Now, query with { Cursor = cursor }));
        var normalized = cursor.Replace('-', '+').Replace('_', '/');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')));
        foreach (var bad in new[] { json.Replace("\"Version\":2", "\"Version\":1"), json.Replace("\"Version\":2,", ""),
            json.Replace("\"Version\":2", "\"Version\":2,\"Version\":2"), json.Replace("\"Version\":2", "\"Version\":2,\"Unknown\":1"), "[]" })
        {
            var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(bad)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPageAsync("branch", Now, query with { Cursor = forged }));
        }
    }

    [Theory]
    [InlineData("day")]
    [InlineData("method")]
    [InlineData("status")]
    [InlineData("limit")]
    [InlineData("receipt")]
    [InlineData("cursor")]
    public async Task Invalid_queries_fail_without_mutating_history(string kind)
    {
        await Seed("one"); var store = await Store(); var before = await Fingerprint();
        var query = kind switch
        {
            "day" => new CashHistoryQuery(BusinessDate: "2026-02-30"),
            "method" => new(PaymentMethod: "Card"),
            "status" => new(Status: "Refunded"),
            "limit" => new(Limit: 51),
            "receipt" => new(ReceiptNumber: " TEST"),
            _ => new(Cursor: "not!base64")
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPageAsync("branch", Now, query));
        Assert.Equal(before, await Fingerprint());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("change")]
    [InlineData("identity")]
    [InlineData("receipt")]
    [InlineData("timestamp")]
    [InlineData("print")]
    [InlineData("missing-numeric")]
    [InlineData("version")]
    public async Task Corrupt_selected_capture_fails_closed_without_disclosing_raw_values_or_mutation(string kind)
    {
        await Seed("one"); var store = await Store();
        var change = kind switch
        {
            "amount" => "UPDATE cash_sales SET result_json=json_set(result_json,'$.AppliedAmount',-1);",
            "change" => "UPDATE cash_sales SET result_json=json_set(result_json,'$.ChangeAmount',0);",
            "identity" => "UPDATE cash_sales SET result_json=json_set(result_json,'$.TableSessionId','private-other');",
            "receipt" => "UPDATE simulated_cash_receipt_jobs SET receipt_number='private-other';",
            "timestamp" => "UPDATE simulated_cash_receipt_jobs SET queued_at='private-invalid';",
            "print" => "UPDATE cash_sales SET result_json=json_set(result_json,'$.PrintStatus','private-unknown');",
            "version" => "UPDATE cash_sales SET result_json=json_set(result_json,'$.ExpectedVersion',0);",
            _ => "UPDATE cash_sales SET result_json=json_remove(result_json,'$.AppliedAmount');"
        };
        await Execute(change); var before = await Fingerprint();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryPageAsync("branch", Now, new()));
        Assert.DoesNotContain("private", exception.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryDetailAsync("branch", "one", Now));
        Assert.Equal(before, await Fingerprint());
    }

    [Fact]
    public async Task Missing_capture_job_is_unavailable_even_when_hidden_by_filter_but_other_branch_corruption_is_isolated()
    {
        await Seed("one"); await Seed("foreign", "other"); var store = await Store();
        await Execute("DELETE FROM simulated_cash_receipt_jobs WHERE branch_id='other';");
        Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now, new())).Items);
        Assert.Null(await store.ReadCashHistoryDetailAsync("branch", "foreign", Now));
        await Execute("DELETE FROM simulated_cash_receipt_jobs WHERE branch_id='branch';");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryPageAsync("branch", Now, new(ReceiptNumber: "NO-MATCH")));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryDetailAsync("branch", "one", Now));
    }

    [Fact]
    public async Task Zero_value_settlement_and_unavailable_legacy_financials_are_not_fabricated_or_rejected()
    {
        await Seed("zero", applied: 0, bill: "{}"); var store = await Store();
        var detail = Assert.IsType<CashHistoryDetail>(await store.ReadCashHistoryDetailAsync("branch", "zero", Now));
        Assert.Equal(0, detail.Transaction.AppliedAmount); Assert.Equal(10, detail.Transaction.ChangeAmount);
        Assert.Null(detail.Financials);
        Assert.Single((await store.ReadCashHistoryPageAsync("branch", Now, new())).Items);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
