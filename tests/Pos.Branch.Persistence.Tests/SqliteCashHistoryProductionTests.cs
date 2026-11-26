using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteCashHistoryProductionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cash-production-{Guid.NewGuid():N}.db");
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
        var sale = new CashSaleResult("session", "employee", "device", 3, "shift", "store", "2026-11-26", 100, 100, 0, "TEST-CASH", "QueuedForSimulation");
        await Sql("""
            INSERT INTO table_sessions VALUES('branch','session','CURRENT','Completed',5);
            INSERT INTO cash_sales VALUES('branch','cash','session',$sale,'{"netTotal":100}');
            INSERT INTO simulated_cash_receipt_jobs VALUES('branch','cash','TEST-CASH',$time);
            """, ("$sale", JsonSerializer.Serialize(sale)), ("$time", Now.ToString("O", CultureInfo.InvariantCulture)));
        return store;
    }
    private static string Payload(string batch, string station, string line) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        orderBatchId = batch,
        tableSessionId = "session",
        tableId = "ORIGINAL",
        productionStationId = station,
        submittedAt = Now,
        lines = new[] { new { orderLineId = line, lineSequence = 3, displayName = "Original <food>", quantity = 4,
            fulfillmentType = "takeaway", comment = "original\ncomment", options = new[] {
                new { optionGroupId = (string?)null, displayName = "Original option", quantityPerItem = 2 } } } }
    });
    private Task Snapshot(string id, string batch, string station, string line, string? json = null, string? hash = null)
    {
        json ??= Payload(batch, station, line);
        hash ??= Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return Sql("""
            INSERT OR IGNORE INTO order_batches(branch_id,order_batch_id,request_id,table_session_id,device_id,employee_id,submitted_at) VALUES('branch',$batch,$request,'session','device','employee',$time);
            INSERT INTO production_order_snapshots VALUES($id,'branch',$request,$batch,'session','ORIGINAL',$station,1,$json,$hash,$time);
            """, ("$id", id), ("$batch", batch), ("$request", "submit-" + batch), ("$station", station),
            ("$json", json), ("$hash", hash), ("$time", Now.ToString("O", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Original_payload_is_independent_of_bill_and_live_table_and_detail_is_scoped()
    {
        var store = await Seed(); var id = "branch:" + new string('s', 500) + ":production:1";
        await Snapshot(id, "batch", "kitchen", "cancelled-line");
        var detail = (await store.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", id, Now))!;
        Assert.Equal("ORIGINAL", detail.Snapshot.TableId); Assert.Equal("submit-batch", detail.Snapshot.SubmittedRequestId);
        Assert.Equal("cancelled-line", Assert.Single(detail.Snapshot.OrderLineIds));
        var line = Assert.Single(detail.Lines); Assert.Equal(4, line.Quantity); Assert.Equal(3, line.LineSequence);
        Assert.Equal("Takeaway", line.FulfillmentType); Assert.Equal("original\ncomment", line.Comment);
        Assert.Null(Assert.Single(line.Options).OptionGroupId); Assert.Equal(2, line.Options[0].QuantityPerItem);
        await Sql("UPDATE table_sessions SET table_id='LATER',version=99;");
        var reopened = new SqliteTableSessionCommandStore(_path);
        var reread = (await reopened.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", id, Now.AddDays(1)))!;
        Assert.Equal(JsonSerializer.Serialize(detail.Snapshot), JsonSerializer.Serialize(reread.Snapshot));
        Assert.Null(await reopened.ReadCashHistoryProductionOrderDetailAsync("other", "cash", id, Now));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reopened.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", "missing", Now));
        Assert.Null(await reopened.ReadCashHistoryProductionOrdersAsync("branch", "missing", Now, new()));
    }

    [Fact]
    public async Task Pagination_binds_subject_filters_limit_schema_and_excludes_appended_snapshots()
    {
        var store = await Seed(); await Snapshot("one", "batch1", "kitchen", "line"); await Snapshot("two", "batch2", "kitchen", "line");
        var query = new CashHistoryProductionQuery(OrderLineId: "line", ProductionStationId: "kitchen", Limit: 1);
        var first = (await store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, query))!;
        Assert.Equal("two", Assert.Single(first.Items).SnapshotId); Assert.NotNull(first.NextCursor);
        await Snapshot("three", "batch3", "kitchen", "line");
        var second = (await store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, query with { Cursor = first.NextCursor }))!;
        Assert.Equal("one", Assert.Single(second.Items).SnapshotId); Assert.Null(second.NextCursor);
        Assert.Empty((await store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, new(OrderLineId: "other")))!.Items);
        Assert.Equal("one", Assert.Single((await store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, new(OrderBatchId: "batch1")))!.Items).SnapshotId);
        foreach (var changed in new[] { query with { Limit = 2 }, query with { OrderLineId = "other" }, query with { ProductionStationId = null }, query with { OrderBatchId = "batch1" } })
        { await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, changed with { Cursor = first.NextCursor })); }
        await Sql("CREATE TABLE unrelated_schema_cookie_change(value TEXT);");
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, query with { Cursor = first.NextCursor }));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("schema")]
    [InlineData("identity")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("quantity")]
    [InlineData("fulfillment")]
    [InlineData("control")]
    [InlineData("timestamp")]
    [InlineData("oversized")]
    [InlineData("duplicate-line")]
    [InlineData("duplicate-sequence")]
    [InlineData("option-count")]
    public async Task Corrupt_original_evidence_is_unavailable_without_fallback(string fault)
    {
        var store = await Seed(); var root = JsonNode.Parse(Payload("batch", "kitchen", "line"))!;
        switch (fault)
        {
            case "schema": root["schemaVersion"] = 2; break;
            case "identity": root["tableId"] = "WRONG"; break;
            case "empty": root["lines"] = new JsonArray(); break;
            case "quantity": root["lines"]![0]!["quantity"] = 0; break;
            case "fulfillment": root["lines"]![0]!["fulfillmentType"] = "DineIn"; break;
            case "control": root["lines"]![0]!["displayName"] = "bad\u0085"; break;
            case "timestamp": root["submittedAt"] = "2026-11-26"; break;
            case "oversized": root["lines"]![0]!["comment"] = new string('x', 1024 * 1024); break;
            case "duplicate-line":
                var sameLine = root["lines"]![0]!.DeepClone(); sameLine["lineSequence"] = 4;
                root["lines"]!.AsArray().Add(sameLine); break;
            case "duplicate-sequence":
                var sameSequence = root["lines"]![0]!.DeepClone(); sameSequence["orderLineId"] = "other";
                root["lines"]!.AsArray().Add(sameSequence); break;
            case "option-count": root["lines"]![0]!["options"]![0]!["quantityPerItem"] = 0; break;
        }
        var json = root.ToJsonString();
        if (fault == "duplicate") { json = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal); }
        await Snapshot("snapshot", "batch", "kitchen", "line", json, fault == "hash" ? new string('0', 64) : null);
        var before = await File.ReadAllBytesAsync(_path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", "snapshot", Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, new()));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Fact]
    public async Task Hash_covers_exact_saved_bytes_and_timestamp_requires_explicit_offset()
    {
        var store = await Seed(); await Snapshot("snapshot", "batch", "kitchen", "line");
        await Sql("UPDATE production_order_snapshots SET content_json=' ' || content_json;");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", "snapshot", Now));
        var json = Payload("batch", "kitchen", "line");
        await Sql("UPDATE production_order_snapshots SET content_json=$json,content_sha256=$hash,created_at='2026-11-26T10:00:00+07:00';",
            ("$json", json), ("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))));
        Assert.Equal(Now, (await store.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", "snapshot", Now))!.Snapshot.SubmittedAt);
        await Sql("UPDATE production_order_snapshots SET created_at='2026-11-26T03:00:00';");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", "snapshot", Now));
    }

    [Fact]
    public async Task Cursor_rejects_changed_cash_identity_and_cannot_survive_vacuum()
    {
        var store = await Seed(); await Snapshot("one", "batch1", "kitchen", "line"); await Snapshot("two", "batch2", "kitchen", "line");
        var query = new CashHistoryProductionQuery(Limit: 1);
        var first = (await store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, query))!;
        var normalized = first.NextCursor!.Replace('-', '+').Replace('_', '/');
        var cursor = JsonNode.Parse(Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')))!;
        cursor["CashRequestId"] = "cash2";
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, query with { Cursor = forged }));
        await Sql("VACUUM;");
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, query with { Cursor = first.NextCursor }));
    }

    [Fact]
    public async Task Reads_do_not_change_database_bytes()
    {
        var store = await Seed(); await Snapshot("snapshot", "batch", "kitchen", "line");
        var before = await File.ReadAllBytesAsync(_path);
        await store.ReadCashHistoryProductionOrdersAsync("branch", "cash", Now, new());
        await store.ReadCashHistoryProductionOrderDetailAsync("branch", "cash", "snapshot", Now);
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
