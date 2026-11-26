using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteCashHistoryEventsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cash-events-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 3, 0, 0, TimeSpan.Zero);
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync(); return store;
    }
    private async Task Sql(string text, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = text;
        foreach (var (name, value) in parameters) { command.Parameters.AddWithValue(name, value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task<SqliteTableSessionCommandStore> Seed(string branch = "branch", string request = "cash", string session = "session")
    {
        var store = await Store();
        var sale = new CashSaleResult(session, "cash-employee", "cash-device", 3, "shift", "store", "2026-11-26", 110, 100, 10, "TEST-" + request, "QueuedForSimulation");
        await Sql("""
            INSERT INTO table_sessions VALUES($branch,$session,'CURRENT-TABLE','Completed',5);
            INSERT INTO cash_sales VALUES($branch,$request,$session,$sale,'{"netTotal":100}');
            INSERT INTO simulated_cash_receipt_jobs VALUES($branch,$request,$receipt,$now);
            """, ("$branch", branch), ("$request", request), ("$session", session), ("$sale", JsonSerializer.Serialize(sale)),
            ("$receipt", sale.ReceiptNumber), ("$now", Now.ToString("O", CultureInfo.InvariantCulture)));
        return store;
    }
    private Task Audit(string id, string branch = "branch", string session = "session", string action = "ChangeTable", string? reason = "original\nreason") =>
        Sql("INSERT INTO table_session_audit VALUES($id,$branch,$request,$session,$action,'Ordering','Ordering','ORIGINAL-A','ORIGINAL-B',3,'original-employee','original-device',$time,$reason);",
            ("$id", id), ("$branch", branch), ("$request", id.Length > 100 ? "operation-composite" : "operation-" + id), ("$session", session), ("$action", action),
            ("$time", Now.ToString("O", CultureInfo.InvariantCulture)), ("$reason", (object?)reason ?? DBNull.Value));
    private static object Evidence(string id, int requested = 2, int cancelled = 1) => new
    {
        Checked = new { OrderLineId = id, NotStarted = requested, InProgress = 0, Completed = 0, NotApplicable = 0 },
        Cancelled = new { OrderLineId = id, NotStarted = cancelled, InProgress = 0, Completed = 0, NotApplicable = 0 },
        RequestedQuantity = requested,
        NotCancelledQuantity = (long)requested - cancelled
    };
    private Task Confirmation(string id, string? json = null, string branch = "branch", string session = "session") =>
        Sql("""
            INSERT INTO cancellation_requests VALUES($branch,$id,$session,'PartiallyConfirmed');
            INSERT INTO cancellation_confirmations VALUES($branch,$id,'confirmed-employee',$time,$json);
            """, ("$branch", branch), ("$id", id), ("$session", session), ("$time", Now.ToString("O", CultureInfo.InvariantCulture)),
            ("$json", json ?? JsonSerializer.Serialize(new[] { Evidence("line-" + id) })));
    private async Task<string> Fingerprint()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        var rows = new List<string>();
        foreach (var table in new[] { "table_sessions", "cash_sales", "simulated_cash_receipt_jobs", "table_session_audit", "cancellation_requests", "cancellation_confirmations", "cloud_sync_outbox" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(table + JsonSerializer.Serialize(values)); }
        }
        return string.Join("\n", rows);
    }

    [Fact]
    public async Task Cash_subject_is_branch_scoped_and_missing_subject_differs_from_empty_evidence()
    {
        var store = await Seed();
        Assert.Null(await store.ReadCashHistoryEventsAsync("other", "cash", Now, new()));
        Assert.Null(await store.ReadCashHistoryEventsAsync("branch", "missing", Now, new()));
        foreach (var source in new[] { "Session", "Cancellation" })
        {
            var page = (await store.ReadCashHistoryEventsAsync("branch", "cash", Now, new(source)))!;
            Assert.Equal(1, page.SchemaVersion); Assert.Equal("branch", page.BranchId); Assert.Equal("cash", page.CashRequestId);
            Assert.Equal("session", page.TableSessionId); Assert.Equal(source, page.Source); Assert.Empty(page.Events); Assert.Null(page.NextCursor);
        }
    }

    [Fact]
    public async Task Recorded_session_payload_ignores_current_table_and_is_read_only_even_for_composite_event_ids()
    {
        var store = await Seed(); var id = new string('x', 520); await Audit(id);
        var original = Assert.Single((await store.ReadCashHistoryEventsAsync("branch", "cash", Now, new()))!.Events);
        Assert.Equal("session:" + id, original.EventId); Assert.Equal("ChangeTable", original.Session!.Action);
        Assert.Equal("ORIGINAL-A", original.Session.PreviousTableId); Assert.Equal("ORIGINAL-B", original.Session.CurrentTableId);
        Assert.Equal("original\nreason", original.Session.Reason); Assert.Equal("original-device", original.DeviceId);
        Assert.Null(original.Cancellation);
        await Sql("UPDATE table_sessions SET table_id='LATER-TABLE',version=99 WHERE branch_id='branch';");
        var before = await Fingerprint();
        var reopened = new SqliteTableSessionCommandStore(_path);
        Assert.Equal(original, Assert.Single((await reopened.ReadCashHistoryEventsAsync("branch", "cash", Now.AddDays(1), new()))!.Events));
        Assert.Equal(before, await Fingerprint());
    }

    [Fact]
    public async Task Cancellation_projects_only_immutable_confirmed_counts_and_unknown_device_without_live_status()
    {
        var store = await Seed();
        await Confirmation("partial", JsonSerializer.Serialize(new[] { Evidence("first", 2, 1), Evidence("zero", 1, 0) }));
        await Confirmation("complete", JsonSerializer.Serialize(new[] { Evidence("full", 2, 2) }));
        var page = (await store.ReadCashHistoryEventsAsync("branch", "cash", Now, new("Cancellation")))!;
        Assert.Equal(new[] { "cancellation:complete", "cancellation:partial" }, page.Events.Select(e => e.EventId));
        Assert.Equal("Confirmed", page.Events[0].Cancellation!.Outcome); Assert.Equal("PartiallyConfirmed", page.Events[1].Cancellation!.Outcome);
        Assert.All(page.Events, e => { Assert.Null(e.DeviceId); Assert.Null(e.Session); Assert.Equal("confirmed-employee", e.EmployeeId); });
        var zero = page.Events[1].Cancellation!.Lines[1]; Assert.Equal(0, zero.Cancelled.NotStarted); Assert.Equal(1, zero.NotCancelledQuantity);
        var frozen = JsonSerializer.Serialize(page.Events);
        await Sql("UPDATE cancellation_requests SET status='Rejected' WHERE branch_id='branch'; UPDATE table_sessions SET table_id='LATER' WHERE branch_id='branch';");
        var before = await Fingerprint();
        Assert.Equal(frozen, JsonSerializer.Serialize((await store.ReadCashHistoryEventsAsync("branch", "cash", Now.AddDays(1), new("Cancellation")))!.Events));
        Assert.Equal(before, await Fingerprint());
    }

    [Theory]
    [InlineData("Session")]
    [InlineData("Cancellation")]
    public async Task Separate_source_highwaters_preserve_pagination_after_append_and_reopen(string source)
    {
        var store = await Seed(); await Seed("other", "other-cash", "other-session");
        if (source == "Session") { await Audit("one"); await Audit("two"); await Audit("foreign", "other", "other-session"); }
        else { await Confirmation("one"); await Confirmation("two"); await Confirmation("foreign", branch: "other", session: "other-session"); }
        var query = new CashHistoryEventQuery(source, 1);
        var page = (await store.ReadCashHistoryEventsAsync("branch", "cash", Now, query))!;
        Assert.NotNull(page.NextCursor); Assert.EndsWith("two", Assert.Single(page.Events).EventId);
        if (source == "Session") { await Audit("late"); } else { await Confirmation("late"); }
        var before = await Fingerprint();
        var next = (await new SqliteTableSessionCommandStore(_path).ReadCashHistoryEventsAsync("branch", "cash", Now.AddDays(1), query with { Cursor = page.NextCursor }))!;
        Assert.EndsWith("one", Assert.Single(next.Events).EventId); Assert.Null(next.NextCursor);
        Assert.EndsWith("late", Assert.Single((await store.ReadCashHistoryEventsAsync("branch", "cash", Now, query))!.Events).EventId);
        Assert.Equal(before, await Fingerprint());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, query with { Source = source == "Session" ? "Cancellation" : "Session", Cursor = page.NextCursor }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, query with { Limit = 2, Cursor = page.NextCursor }));
        await Seed("branch", "different", "different-session");
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryEventsAsync("branch", "different", Now, query with { Cursor = page.NextCursor }));
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(page.NextCursor!.Replace('-', '+').Replace('_', '/').PadRight((page.NextCursor.Length + 3) / 4 * 4, '=')));
        var node = JsonNode.Parse(decoded)!; node["BranchId"] = "other";
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, query with { Cursor = forged }));
    }

    [Fact]
    public async Task Schema_maintenance_invalidates_old_event_cursors()
    {
        var store = await Seed(); await Audit("one"); await Audit("two");
        var query = new CashHistoryEventQuery(Limit: 1);
        var page = (await store.ReadCashHistoryEventsAsync("branch", "cash", Now, query))!;
        await Sql("VACUUM;");
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, query with { Cursor = page.NextCursor }));
        Assert.Single((await store.ReadCashHistoryEventsAsync("branch", "cash", Now, query))!.Events);
    }

    [Theory]
    [InlineData("unknown-action")]
    [InlineData("invalid-time")]
    [InlineData("zero-version")]
    [InlineData("private-control")]
    public async Task Corrupt_session_evidence_is_unavailable_without_affecting_foreign_subjects(string corruption)
    {
        var store = await Seed(); await Seed("other", "foreign-cash", "foreign-session"); await Audit("broken");
        await Sql(corruption switch
        {
            "unknown-action" => "UPDATE table_session_audit SET action='PRIVATE-UNKNOWN';",
            "invalid-time" => "UPDATE table_session_audit SET occurred_at='PRIVATE-INVALID';",
            "zero-version" => "UPDATE table_session_audit SET table_version=0;",
            _ => "UPDATE table_session_audit SET reason=char(0);"
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, new()));
        Assert.Empty((await store.ReadCashHistoryEventsAsync("other", "foreign-cash", Now, new()))!.Events);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("all-zero")]
    [InlineData("over-cancel")]
    [InlineData("wrong-remaining")]
    [InlineData("wrong-line")]
    [InlineData("negative")]
    [InlineData("missing-field")]
    [InlineData("duplicate-property")]
    [InlineData("oversize")]
    public async Task Malformed_confirmation_counts_fail_closed_without_fabricating_event(string corruption)
    {
        var store = await Seed();
        var node = JsonNode.Parse(JsonSerializer.Serialize(new[] { Evidence("line") }))!;
        string json;
        switch (corruption)
        {
            case "empty": json = "[]"; break;
            case "duplicate": json = JsonSerializer.Serialize(new[] { Evidence("line"), Evidence("line") }); break;
            case "all-zero": json = JsonSerializer.Serialize(new[] { Evidence("line", 2, 0) }); break;
            case "over-cancel": node[0]!["Cancelled"]!["NotStarted"] = 3; json = node.ToJsonString(); break;
            case "wrong-remaining": node[0]!["NotCancelledQuantity"] = 7; json = node.ToJsonString(); break;
            case "wrong-line": node[0]!["Cancelled"]!["OrderLineId"] = "other"; json = node.ToJsonString(); break;
            case "negative": node[0]!["Checked"]!["Completed"] = -1; json = node.ToJsonString(); break;
            case "missing-field": node[0]!.AsObject().Remove("Checked"); json = node.ToJsonString(); break;
            case "duplicate-property": json = node.ToJsonString().Replace("\"RequestedQuantity\":2", "\"RequestedQuantity\":2,\"RequestedQuantity\":2", StringComparison.Ordinal); break;
            default: json = new string(' ', 1024 * 1024 + 1) + node.ToJsonString(); break;
        }
        await Confirmation("corrupt", json);
        var before = await Fingerprint();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, new("Cancellation")));
        Assert.Equal(before, await Fingerprint());
    }

    [Fact]
    public async Task Orphan_confirmation_is_not_silently_an_empty_page()
    {
        var store = await Seed(); await Confirmation("orphan");
        await Sql("PRAGMA foreign_keys=OFF; DELETE FROM cancellation_requests WHERE branch_id='branch';");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, new("Cancellation")));
    }

    [Theory]
    [InlineData("All", 20)]
    [InlineData("session", 20)]
    [InlineData("Session", 0)]
    [InlineData("Cancellation", 51)]
    public async Task Invalid_queries_are_rejected(string source, int limit)
    {
        var store = await Seed();
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryEventsAsync("branch", "cash", Now, new(source, limit)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
