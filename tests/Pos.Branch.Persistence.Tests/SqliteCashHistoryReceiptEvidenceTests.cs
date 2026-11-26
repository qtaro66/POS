using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteCashHistoryReceiptEvidenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cash-receipt-evidence-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 3, 0, 0, TimeSpan.Zero);
    private async Task Sql(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) { command.Parameters.AddWithValue(name, value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task<SqliteTableSessionCommandStore> Seed()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        await Cash("branch", "cash", "session", "TEST-CASH"); return store;
    }
    private Task Cash(string branch, string request, string session, string receipt)
    {
        var sale = new CashSaleResult(session, "employee", "device", 3, "shift", "store", "2026-11-26", 120, 100, 20, receipt, "QueuedForSimulation");
        return Sql("""
            INSERT INTO table_sessions VALUES($branch,$session,'TABLE','Completed',5);
            INSERT INTO cash_sales VALUES($branch,$request,$session,$sale,'{"items":[{"displayName":"Original <food>"}]}');
            INSERT INTO simulated_cash_receipt_jobs VALUES($branch,$request,$receipt,$time);
            """, ("$branch", branch), ("$request", request), ("$session", session), ("$receipt", receipt),
            ("$sale", JsonSerializer.Serialize(sale)), ("$time", Now.ToString("O", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Missing_optional_table_is_unrecorded_without_creating_schema_or_changing_database()
    {
        var store = await Seed(); var before = await File.ReadAllBytesAsync(_path);
        var evidence = (await store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now))!;
        Assert.Null(evidence.Simulation); Assert.Equal("TEST-CASH", evidence.ReceiptNumber);
        Assert.Equal(Now, evidence.QueuedAt); Assert.Equal("QueuedForSimulation", evidence.CapturedPrintStatus);
        Assert.Equal("session", evidence.TableSessionId); Assert.Equal("cash", evidence.CashRequestId);
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        using var check = connection.CreateCommand(); check.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='test_receipt_simulation_results';";
        Assert.Equal(0L, await check.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Missing_optional_result_does_not_infer_simulation_and_parent_is_branch_scoped()
    {
        var store = await Seed(); Assert.Null(await store.SimulateReceiptAsync("branch", "missing", false, default));
        var before = await File.ReadAllBytesAsync(_path);
        Assert.Null((await store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now))!.Simulation);
        Assert.Null(await store.ReadCashHistoryReceiptEvidenceAsync("foreign", "cash", Now));
        Assert.Null(await store.ReadCashHistoryReceiptEvidenceAsync("branch", "missing", Now));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_simulation_result_is_read_as_recorded_without_rerender_or_repair(bool fail)
    {
        var store = await Seed(); var recorded = (await store.SimulateReceiptAsync("branch", "TEST-CASH", fail, default))!;
        await Sql("UPDATE cash_sales SET bill_snapshot_json='{}'; UPDATE table_sessions SET table_id='LATER';");
        var before = await File.ReadAllBytesAsync(_path);
        var evidence = (await store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now.AddDays(1)))!;
        Assert.Equal(recorded.Status, evidence.Simulation!.Status); Assert.Equal(recorded.Text, evidence.Simulation.Text);
        Assert.Equal(Now.AddDays(1), evidence.AsOf); Assert.Equal(Now, evidence.QueuedAt);
        if (!fail) { Assert.Contains("Original <food>", evidence.Simulation.Text); }
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
        var reopened = new SqliteTableSessionCommandStore(_path);
        Assert.Equal(evidence.Simulation, (await reopened.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now))!.Simulation);
    }

    [Fact]
    public async Task Receipt_lookup_uses_captured_branch_and_receipt_identity_only()
    {
        var store = await Seed();
        await Cash("other", "cash", "session", "TEST-CASH"); await Cash("branch", "cash2", "session2", "TEST-CASH-2");
        await store.SimulateReceiptAsync("other", "TEST-CASH", true, default);
        await store.SimulateReceiptAsync("branch", "TEST-CASH-2", true, default);
        Assert.Null((await store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now))!.Simulation);
        Assert.Equal("Failed", (await store.ReadCashHistoryReceiptEvidenceAsync("other", "cash", Now))!.Simulation!.Status);
        Assert.Equal("Failed", (await store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash2", Now))!.Simulation!.Status);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing-text")]
    [InlineData("blank")]
    [InlineData("failed-text")]
    [InlineData("control")]
    [InlineData("oversized")]
    [InlineData("blob")]
    public async Task Invalid_recorded_simulation_is_unavailable_without_mutation(string fault)
    {
        var store = await Seed(); await store.SimulateReceiptAsync("branch", "TEST-CASH", false, default);
        var status = fault == "unknown" ? "PRIVATE-UNKNOWN" : fault == "failed-text" ? "Failed" : "Simulated";
        object content = fault switch
        {
            "missing-text" => DBNull.Value,
            "blank" => " \r\n\t",
            "control" => "Private\u0085control",
            "oversized" => new string('ก', 43691),
            "blob" => Encoding.UTF8.GetBytes("Private blob"),
            _ => "Private recorded text"
        };
        await Sql("PRAGMA ignore_check_constraints=ON; UPDATE test_receipt_simulation_results SET status=$status,content=$content;",
            ("$status", status), ("$content", content));
        var before = await File.ReadAllBytesAsync(_path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Fact]
    public async Task Bound_is_utf8_bytes_and_original_plaintext_whitespace_is_preserved()
    {
        var store = await Seed(); await store.SimulateReceiptAsync("branch", "TEST-CASH", false, default);
        var original = new string('ก', 43690) + "\r\n";
        Assert.Equal(128 * 1024, Encoding.UTF8.GetByteCount(original));
        await Sql("UPDATE test_receipt_simulation_results SET content=$text;", ("$text", original));
        Assert.Equal(original, (await store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now))!.Simulation!.Text);
        await Sql("UPDATE test_receipt_simulation_results SET content=$text;", ("$text", original + "\t"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryReceiptEvidenceAsync("branch", "cash", Now));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
