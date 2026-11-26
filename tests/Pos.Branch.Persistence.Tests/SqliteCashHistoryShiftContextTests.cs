using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteCashHistoryShiftContextTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cash-shift-context-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 3, 0, 0, TimeSpan.Zero);
    private static StoreLifecycleCommand Command(string request, StoreLifecycleAction action, decimal? cash = null,
        string shift = "shift", string store = "store") => new(request, store, shift, action, cash,
            new("recorded-employee", "recorded-device", Now, new(2026, 11, 26)));
    private async Task Sql(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) { command.Parameters.AddWithValue(name, value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Scalar(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }
    private async Task<SqliteTableSessionCommandStore> Seed()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        var sale = new CashSaleResult("session", "cash-employee", "cash-device", 3, "shift", "store", "2026-11-26",
            100, 100, 0, "TEST-CASH", "QueuedForSimulation");
        await Sql("""
            INSERT INTO table_sessions VALUES('branch','session','TABLE','Completed',5);
            INSERT INTO cash_sales VALUES('branch','cash','session',$sale,'{"netTotal":100}');
            INSERT INTO simulated_cash_receipt_jobs VALUES('branch','cash','TEST-CASH',$time);
            """, ("$sale", JsonSerializer.Serialize(sale)), ("$time", Now.ToString("O", CultureInfo.InvariantCulture)));
        return store;
    }

    [Fact]
    public async Task Original_open_and_unreconciled_close_survive_reopen_and_ignore_other_shift_and_store()
    {
        var store = await Seed();
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift));
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, 999, "second"));
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore, 590, "second"));
        await store.ExecuteStoreLifecycleAsync("branch", Command("new-store", StoreLifecycleAction.OpenStore, 700, store: "later"));
        await store.ExecuteStoreLifecycleAsync("foreign", Command("open", StoreLifecycleAction.OpenStore, 900));
        var reopened = new SqliteTableSessionCommandStore(_path);
        var before = await File.ReadAllBytesAsync(_path);
        var value = (await reopened.ReadCashHistoryShiftContextAsync("branch", "cash", Now.AddDays(1)))!;
        Assert.Equal("store", value.StoreSessionId); Assert.Equal("shift", value.ShiftId); Assert.Equal("2026-11-26", value.BusinessDate);
        Assert.Equal(new[] { "open", "close" }, value.Entries.Select(entry => entry.RequestId));
        Assert.Equal("recorded-employee", value.Entries[0].EmployeeId); Assert.Equal("recorded-device", value.Entries[0].DeviceId);
        Assert.Equal(500, value.Entries[0].CashAmount); Assert.Null(value.Entries[0].Closing);
        var closing = value.Entries[1].Closing!;
        Assert.False(closing.ClosesStore); Assert.Equal(600, closing.ExpectedCashAmount);
        Assert.Null(closing.ActualCashAmount); Assert.Null(closing.CashDifference);
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
        Assert.Null(await reopened.ReadCashHistoryShiftContextAsync("foreign", "cash", Now));
        Assert.Null(await reopened.ReadCashHistoryShiftContextAsync("branch", "missing", Now));
    }

    [Fact]
    public async Task Open_shift_does_not_project_previous_shift_closing_and_close_store_retains_original_difference()
    {
        var store = await Seed();
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift, 590));
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, shift: "second"));
        await Sql("UPDATE cash_sales SET result_json=json_set(result_json,'$.ShiftId','second');");
        var openingOnly = (await store.ReadCashHistoryShiftContextAsync("branch", "cash", Now))!;
        Assert.Equal("OpenShift", Assert.Single(openingOnly.Entries).Action);
        Assert.Null(openingOnly.Entries[0].CashAmount); Assert.Null(openingOnly.Entries[0].Closing);
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore, 595, "second"));
        var closed = (await store.ReadCashHistoryShiftContextAsync("branch", "cash", Now))!.Entries[1].Closing!;
        Assert.True(closed.ClosesStore); Assert.Equal(600, closed.ExpectedCashAmount);
        Assert.Equal(595, closed.ActualCashAmount); Assert.Equal(-5, closed.CashDifference);
    }

    [Fact]
    public async Task Empty_or_closing_only_evidence_does_not_invent_missing_operations()
    {
        var store = await Seed();
        await Sql("UPDATE cash_sales SET result_json=json_set(result_json,'$.StoreSessionId','legacy');");
        Assert.Empty((await store.ReadCashHistoryShiftContextAsync("branch", "cash", Now))!.Entries);
        await Sql("UPDATE cash_sales SET result_json=json_set(result_json,'$.StoreSessionId','store');");
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift));
        await Sql("DELETE FROM store_lifecycle_commands WHERE request_id='open';");
        var entry = Assert.Single((await store.ReadCashHistoryShiftContextAsync("branch", "cash", Now))!.Entries);
        Assert.Equal("CloseShift", entry.Action); Assert.NotNull(entry.Closing);
    }

    [Theory]
    [InlineData("missing-cash")]
    [InlineData("missing-closed")]
    [InlineData("business-date")]
    [InlineData("opening-date")]
    [InlineData("opening-closing")]
    [InlineData("request")]
    [InlineData("timestamp")]
    [InlineData("actor-control")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    [InlineData("unknown-action")]
    [InlineData("blob-command")]
    [InlineData("blob-result")]
    public async Task Malformed_opening_is_unavailable_without_repair(string fault)
    {
        var store = await Seed();
        var command = JsonNode.Parse(await Scalar("SELECT command_json FROM store_lifecycle_commands WHERE request_id='open';"))!;
        var result = JsonNode.Parse(await Scalar("SELECT result_json FROM store_lifecycle_commands WHERE request_id='open';"))!;
        switch (fault)
        {
            case "missing-cash": command.AsObject().Remove("CashAmount"); break;
            case "missing-closed": result.AsObject().Remove("IsClosed"); break;
            case "business-date": result["BusinessDate"] = "2026-11-27"; break;
            case "opening-date": command["Context"]!["BranchLocalDate"] = "2026-11-27"; break;
            case "opening-closing": result["Closing"] = new JsonObject(); break;
            case "request": command["RequestId"] = "wrong"; break;
            case "timestamp": command["Context"]!["OccurredAt"] = "2026-11-26"; break;
            case "actor-control": command["Context"]!["EmployeeId"] = "bad\u0085"; break;
            case "oversized": command["padding"] = new string('x', 1024 * 1024); break;
            case "unknown-action": command["Action"] = 99; break;
        }
        var json = command.ToJsonString();
        if (fault == "duplicate") { json = json.Replace("\"Action\":0", "\"Action\":0,\"Action\":0", StringComparison.Ordinal); }
        await Sql("UPDATE store_lifecycle_commands SET command_json=$command,result_json=$result WHERE request_id='open';",
            ("$command", json), ("$result", result.ToJsonString()));
        if (fault == "blob-command") { await Sql("UPDATE store_lifecycle_commands SET command_json=CAST(command_json AS BLOB) WHERE request_id='open';"); }
        if (fault == "blob-result") { await Sql("UPDATE store_lifecycle_commands SET result_json=CAST(result_json AS BLOB) WHERE request_id='open';"); }
        var before = await File.ReadAllBytesAsync(_path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryShiftContextAsync("branch", "cash", Now));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Theory]
    [InlineData("missing-expected")]
    [InlineData("actual")]
    [InlineData("difference")]
    [InlineData("context")]
    [InlineData("closes-store")]
    [InlineData("negative")]
    [InlineData("overflow")]
    public async Task Incoherent_closing_is_unavailable(string fault)
    {
        var store = await Seed(); await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift, 595));
        var result = JsonNode.Parse(await Scalar("SELECT result_json FROM store_lifecycle_commands WHERE request_id='close';"))!;
        var closing = result["Closing"]!;
        switch (fault)
        {
            case "missing-expected": closing.AsObject().Remove("ExpectedCashAmount"); break;
            case "actual": closing["ActualCashAmount"] = 599; break;
            case "difference": closing["CashDifference"] = 5; break;
            case "context": closing["Context"]!["DeviceId"] = "other"; break;
            case "closes-store": closing["ClosesStore"] = true; break;
            case "negative": closing["ExpectedCashAmount"] = -1; break;
            case "overflow": closing["ExpectedCashAmount"] = 1e40; break;
        }
        await Sql("UPDATE store_lifecycle_commands SET result_json=$result WHERE request_id='close';", ("$result", result.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryShiftContextAsync("branch", "cash", Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extra_or_duplicate_operations_are_not_silently_truncated(bool third)
    {
        var store = await Seed();
        if (third) { await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift)); }
        await Sql("""
            INSERT INTO store_lifecycle_commands(branch_id,request_id,store_session_id,command_json,result_json)
            SELECT branch_id,'extra',store_session_id,json_set(command_json,'$.RequestId','extra'),result_json
            FROM store_lifecycle_commands WHERE request_id='open';
            """);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryShiftContextAsync("branch", "cash", Now));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
