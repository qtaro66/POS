using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteClosingLifecycleFilterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"context-filter-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync(); return store;
    }
    private async Task Close(string request, string branch = "branch", bool parent = false, bool context = false, bool legacy = false)
    {
        var store = await Store();
        var open = new StoreLifecycleCommand("open-" + request, "store-" + request, "shift-" + request,
            StoreLifecycleAction.OpenStore, 500, new("employee", "device", Now, new(2026, 11, 22)));
        await store.ExecuteStoreLifecycleAsync(branch, open);
        await store.ExecuteStoreLifecycleAsync(branch, open with { RequestId = request, Action = StoreLifecycleAction.CloseStore, CashAmount = 490 });
        if (parent)
        {
            var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync(branch, "base", Now, Lease));
            Assert.Equal(request, lease.RequestId); Assert.True(await store.ConfirmClosingReportAsync(lease, Now));
        }
        if (context)
        {
            var lease = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync(branch, "context", Now, Lease));
            Assert.Equal(request, lease.RequestId); Assert.True(await store.ConfirmClosingLifecycleAsync(lease, Now));
        }
        if (legacy) { await Sql($"DELETE FROM closing_report_context_outbox WHERE branch_id='{branch}' AND request_id='{request}'; DELETE FROM closing_report_lifecycle_snapshots WHERE branch_id='{branch}' AND request_id='{request}';"); }
    }
    private async Task Sql(string text)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = text; await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Fingerprint()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        var result = new List<string>();
        foreach (var table in new[] { "closing_report_outbox", "closing_report_lifecycle_snapshots", "closing_report_context_outbox" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY branch_id,request_id;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { result.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)))); }
        }
        return string.Join("\n", result);
    }
    private static string Encode(JsonNode value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static JsonNode Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return JsonNode.Parse(Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')))!;
    }

    [Fact]
    public async Task Independent_filters_include_parent_delivered_context_pending_and_legacy_only_when_explicit()
    {
        await Close("confirmed", parent: true, context: true);
        await Close("awaiting-context", parent: true);
        await Close("awaiting-parent");
        await Close("legacy", legacy: true);
        await Close("foreign", "other");
        var store = await Store(); var before = await Fingerprint();
        var pending = await store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Pending"));
        Assert.Equal(new[] { "awaiting-parent", "awaiting-context" }, pending.Items.Select(i => i.RequestId));
        Assert.Equal("awaiting-context", Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new(Status: "Delivered", LifecycleStatus: "Pending"))).Items).RequestId);
        Assert.Equal("awaiting-parent", Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new(Status: "Pending", LifecycleStatus: "Pending"))).Items).RequestId);
        Assert.Equal("confirmed", Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Delivered"))).Items).RequestId);
        Assert.Equal("legacy", Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "NotRecorded"))).Items).RequestId);
        Assert.Empty((await store.ReadClosingReportPageAsync("branch", Now, new(Status: "Pending", LifecycleStatus: "Delivered"))).Items);
        Assert.Equal(4, (await store.ReadClosingReportPageAsync("branch", Now, new())).Items.Count);
        Assert.Equal(before, await Fingerprint());
    }

    [Fact]
    public async Task Migration_34_backfills_real_context_for_filtering_but_does_not_fabricate_legacy_capture()
    {
        await Close("legacy", parent: true, legacy: true);
        await Close("recorded", parent: true);
        await Sql("DROP TABLE closing_report_context_outbox; PRAGMA user_version=34;");
        var upgraded = await Store();
        Assert.Equal("recorded", Assert.Single((await upgraded.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Pending"))).Items).RequestId);
        Assert.Equal("legacy", Assert.Single((await upgraded.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "NotRecorded"))).Items).RequestId);
        Assert.Empty((await upgraded.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Delivered"))).Items);
    }

    [Fact]
    public async Task Lifecycle_filter_combines_with_saved_scope_without_recalculating_snapshot()
    {
        await Close("one", parent: true); await Close("two");
        var store = await Store();
        var query = new ClosingReportQuery("Delivered", "store-one", "shift-one", "2026-11-22", LifecycleStatus: "Pending");
        Assert.Equal("one", Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, query)).Items).RequestId);
        Assert.Empty((await store.ReadClosingReportPageAsync("branch", Now, query with { BusinessDate = "2026-11-21" })).Items);
        Assert.Empty((await store.ReadClosingReportPageAsync("branch", Now, query with { ShiftId = "shift-two" })).Items);
        Assert.Empty((await store.ReadClosingReportPageAsync("branch", Now, query with { StoreSessionId = "' OR 1=1 --" })).Items);
    }

    [Fact]
    public async Task Pending_membership_tracks_live_retry_expiry_and_confirmation_without_changing_snapshot()
    {
        await Close("one", parent: true); var store = await Store();
        var before = JsonSerializer.Serialize((await store.ReadClosingReportDetailAsync("branch", "one", Now))!.Lifecycle);
        var claim = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "worker", Now, Lease));
        foreach (var time in new[] { Now, Now.AddMinutes(1) })
        {
            Assert.Single((await store.ReadClosingReportPageAsync("branch", time, new(LifecycleStatus: "Pending"))).Items);
        }
        Assert.True(await store.RetryClosingLifecycleAsync(claim, Now, Lease, ClosingLifecycleFailure.TransportUnavailable));
        Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Pending"))).Items);
        var reopened = await Store();
        var retry = Assert.IsType<ClosingLifecycleLease>(await reopened.ClaimClosingLifecycleAsync("branch", "restart", Now.AddMinutes(1), Lease));
        Assert.True(await reopened.ConfirmClosingLifecycleAsync(retry, Now.AddMinutes(1)));
        Assert.Empty((await reopened.ReadClosingReportPageAsync("branch", Now.AddMinutes(1), new(LifecycleStatus: "Pending"))).Items);
        Assert.Single((await reopened.ReadClosingReportPageAsync("branch", Now.AddMinutes(1), new(LifecycleStatus: "Delivered"))).Items);
        Assert.Equal(before, JsonSerializer.Serialize((await reopened.ReadClosingReportDetailAsync("branch", "one", Now.AddMinutes(1)))!.Lifecycle));
    }

    [Fact]
    public async Task Cursor_binds_lifecycle_filter_and_highwater_across_append_reopen_and_confirmation()
    {
        await Close("one", parent: true); await Close("two", parent: true); await Close("three", parent: true);
        var store = await Store(); var query = new ClosingReportQuery(Limit: 1, LifecycleStatus: "Pending");
        var first = await store.ReadClosingReportPageAsync("branch", Now, query);
        Assert.Equal("three", Assert.Single(first.Items).RequestId);
        var cursor = Decode(first.NextCursor!); Assert.Equal(2, cursor["Version"]!.GetValue<int>()); Assert.Equal("Pending", cursor["LifecycleStatus"]!.GetValue<string>());
        foreach (var status in new[] { "All", "Delivered", "NotRecorded" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, query with { LifecycleStatus = status, Cursor = first.NextCursor }));
        }
        await Close("four", parent: true);
        var context = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "context", Now, Lease));
        Assert.Equal("one", context.RequestId); Assert.True(await store.ConfirmClosingLifecycleAsync(context, Now));
        var reopened = await Store();
        var second = await reopened.ReadClosingReportPageAsync("branch", Now, query with { Cursor = first.NextCursor });
        Assert.Equal("two", Assert.Single(second.Items).RequestId); Assert.Null(second.NextCursor);
        Assert.Equal("four", Assert.Single((await reopened.ReadClosingReportPageAsync("branch", Now, query)).Items).RequestId);
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.ReadClosingReportPageAsync("other", Now, query with { Cursor = first.NextCursor }));
    }

    [Theory]
    [InlineData("legacy-version")]
    [InlineData("missing-lifecycle")]
    [InlineData("unknown-field")]
    [InlineData("null-lifecycle")]
    [InlineData("wrong-lifecycle")]
    [InlineData("duplicate")]
    public async Task Cursor_rejects_legacy_missing_duplicate_or_tampered_lifecycle_scope(string mutation)
    {
        await Close("one"); await Close("two"); var store = await Store();
        var query = new ClosingReportQuery(Limit: 1, LifecycleStatus: "Pending");
        var first = await store.ReadClosingReportPageAsync("branch", Now, query); var cursor = Decode(first.NextCursor!);
        if (mutation == "legacy-version") { cursor["Version"] = 1; cursor.AsObject().Remove("LifecycleStatus"); }
        if (mutation == "missing-lifecycle") { cursor.AsObject().Remove("LifecycleStatus"); }
        if (mutation == "unknown-field") { cursor.AsObject().Remove("LifecycleStatus"); cursor["Other"] = "Pending"; }
        if (mutation == "null-lifecycle") { cursor["LifecycleStatus"] = null; }
        if (mutation == "wrong-lifecycle") { cursor["LifecycleStatus"] = "Delivered"; }
        var encoded = Encode(cursor);
        if (mutation == "duplicate") { encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor.ToJsonString().Replace("\"LifecycleStatus\":\"Pending\"", "\"LifecycleStatus\":\"Pending\",\"LifecycleStatus\":\"Pending\""))).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, query with { Cursor = encoded }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pending")]
    [InlineData("Unknown")]
    [InlineData(" Pending")]
    public async Task Noncanonical_lifecycle_filters_are_rejected(string status)
    {
        var store = await Store();
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: status)));
    }

    [Theory]
    [InlineData("missing-job")]
    [InlineData("orphan-job")]
    [InlineData("wrong-identity")]
    [InlineData("invalid-confirmation")]
    [InlineData("invalid-available")]
    [InlineData("invalid-lease")]
    [InlineData("unconfirmed-parent")]
    [InlineData("unclaimed-confirmation")]
    public async Task Corrupt_local_context_is_unavailable_for_every_filter_but_foreign_corruption_is_isolated(string corruption)
    {
        await Close("one", parent: true); await Close("foreign", "other", parent: true);
        var mutate = corruption switch
        {
            "missing-job" => "DELETE FROM closing_report_context_outbox WHERE branch_id=$branch;",
            "orphan-job" => "PRAGMA foreign_keys=OFF; DELETE FROM closing_report_lifecycle_snapshots WHERE branch_id=$branch;",
            "wrong-identity" => "UPDATE closing_report_context_outbox SET context_id='wrong-' || branch_id WHERE branch_id=$branch;",
            "invalid-confirmation" => "UPDATE closing_report_context_outbox SET delivered_at='private-invalid',attempt_count=1 WHERE branch_id=$branch;",
            "invalid-available" => "UPDATE closing_report_context_outbox SET available_at='private-invalid' WHERE branch_id=$branch;",
            "invalid-lease" => "UPDATE closing_report_context_outbox SET lease_id='lease',lease_owner='worker',lease_expires_at='private-invalid',attempt_count=1 WHERE branch_id=$branch;",
            "unconfirmed-parent" => "UPDATE closing_report_context_outbox SET delivered_at='2026-11-22T03:00:00Z',attempt_count=1 WHERE branch_id=$branch; UPDATE closing_report_outbox SET delivered_at=NULL WHERE branch_id=$branch;",
            _ => "UPDATE closing_report_context_outbox SET delivered_at='2026-11-22T03:00:00Z',attempt_count=0 WHERE branch_id=$branch;"
        };
        await Sql(mutate.Replace("$branch", "'other'"));
        var store = await Store();
        foreach (var filter in new[] { "All", "Pending", "Delivered", "NotRecorded" })
        {
            await store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: filter));
        }
        await Sql(mutate.Replace("$branch", "'branch'"));
        var before = await Fingerprint();
        foreach (var filter in new[] { "All", "Pending", "Delivered", "NotRecorded" })
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: filter)));
        }
        Assert.Equal(before, await Fingerprint());
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
