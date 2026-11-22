using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteClosingReportStatusTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"report-status-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private async Task<SqliteTableSessionCommandStore> Close(string request = "close", string branch = "branch")
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        var command = new StoreLifecycleCommand("open-" + request, "store-" + request, "shift-" + request,
            StoreLifecycleAction.OpenStore, 500, new("employee", "device", Now, new(2026, 11, 22)));
        await store.ExecuteStoreLifecycleAsync(branch, command);
        await store.ExecuteStoreLifecycleAsync(branch, command with { RequestId = request, Action = StoreLifecycleAction.CloseStore, CashAmount = 490 });
        return store;
    }
    private static async Task<ClosingReportLease> Parent(SqliteTableSessionCommandStore store, string branch = "branch")
    {
        var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync(branch, "parent-worker", Now, Lease));
        Assert.True(await store.ConfirmClosingReportAsync(lease, Now)); return lease;
    }
    private async Task Sql(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Foreign Keys=True");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Fingerprint()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Mode=ReadOnly"); await connection.OpenAsync();
        var values = new List<string>();
        foreach (var table in new[] { "closing_report_outbox", "closing_report_lifecycle_snapshots", "closing_report_context_outbox" })
        {
            using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY branch_id,request_id;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { values.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)))); }
        }
        return string.Join("\n", values);
    }
    private async Task<ClosingReportListItem> Item(SqliteTableSessionCommandStore store, DateTimeOffset now, string state)
    {
        var before = await Fingerprint();
        var page = await store.ReadClosingReportPageAsync("branch", now, new());
        var item = Assert.Single(page.Items); var detail = (await store.ReadClosingReportDetailAsync("branch", item.RequestId, now))!;
        Assert.Equal(state, item.LifecycleDelivery!.DeliveryState);
        Assert.Equal(item, detail.Report); Assert.Equal(item.LifecycleDelivery, detail.LifecycleDelivery);
        Assert.Equal(before, await Fingerprint()); return item;
    }

    [Fact]
    public async Task Page_projects_all_context_states_with_same_receipt_as_detail_and_no_read_mutation()
    {
        var store = await Close();
        var queued = await Item(store, Now, "Queued");
        Assert.Equal("Queued", queued.DeliveryState); Assert.Equal(0, queued.LifecycleDelivery!.AttemptCount);
        await Parent(store);
        var parentOnly = await Item(store, Now, "Queued"); Assert.Equal("Delivered", parentOnly.DeliveryState);
        var original = (await store.ReadClosingReportDetailAsync("branch", "close", Now))!;
        var frozen = JsonSerializer.Serialize(original.Snapshot) + JsonSerializer.Serialize(original.Lifecycle);
        var first = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "PRIVATE-WORKER", Now, Lease));
        var flight = await Item(store, Now, "InFlight");
        Assert.Equal(1, flight.LifecycleDelivery!.AttemptCount); Assert.Equal(Now.AddMinutes(1), flight.LifecycleDelivery.LeaseExpiresAt);
        var publicJson = JsonSerializer.Serialize(flight);
        Assert.DoesNotContain("PRIVATE-WORKER", publicJson); Assert.DoesNotContain(first.LeaseId, publicJson);
        Assert.True(await store.RetryClosingLifecycleAsync(first, Now, TimeSpan.FromSeconds(10), ClosingLifecycleFailure.ParentUnavailable));
        var delayed = await Item(store, Now, "RetryScheduled"); Assert.Equal("ParentUnavailable", delayed.LifecycleDelivery!.LastFailure);
        var second = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "another-worker", Now.AddSeconds(10), Lease));
        var reopened = new SqliteTableSessionCommandStore(_path);
        var expired = await Item(reopened, Now.AddSeconds(70), "LeaseExpired");
        Assert.Equal(2, expired.LifecycleDelivery!.AttemptCount);
        var third = Assert.IsType<ClosingLifecycleLease>(await reopened.ClaimClosingLifecycleAsync("branch", "restart-worker", Now.AddSeconds(70), Lease));
        Assert.False(await reopened.ConfirmClosingLifecycleAsync(second, Now.AddSeconds(71)));
        Assert.True(await reopened.ConfirmClosingLifecycleAsync(third, Now.AddSeconds(71)));
        var complete = await Item(reopened, Now.AddSeconds(71), "Delivered");
        Assert.Equal(3, complete.LifecycleDelivery!.AttemptCount); Assert.Null(complete.LifecycleDelivery.LastFailure);
        Assert.Equal(Now.AddSeconds(71), complete.LifecycleDelivery.DeliveredAt);
        Assert.Equal(original.Report with { LifecycleDelivery = complete.LifecycleDelivery }, complete);
        var after = (await reopened.ReadClosingReportDetailAsync("branch", "close", Now.AddSeconds(71)))!;
        Assert.Equal(frozen, JsonSerializer.Serialize(after.Snapshot) + JsonSerializer.Serialize(after.Lifecycle));
        Assert.Equal(first.PayloadJson, third.PayloadJson);
    }

    [Fact]
    public async Task Historical_rows_project_explicit_null_while_recorded_pending_rows_remain_distinct()
    {
        var store = await Close("legacy"); await Parent(store);
        await Sql("DELETE FROM closing_report_context_outbox WHERE request_id='legacy'; DELETE FROM closing_report_lifecycle_snapshots WHERE request_id='legacy';");
        await Close("recorded");
        var before = await Fingerprint();
        var historical = Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "NotRecorded"))).Items);
        Assert.Equal("legacy", historical.RequestId); Assert.Null(historical.LifecycleDelivery);
        var detail = (await store.ReadClosingReportDetailAsync("branch", "legacy", Now))!;
        Assert.Null(detail.Report.LifecycleDelivery); Assert.Null(detail.LifecycleDelivery); Assert.Null(detail.Lifecycle);
        var pending = Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Pending"))).Items);
        Assert.Equal("recorded", pending.RequestId); Assert.Equal("Queued", pending.LifecycleDelivery!.DeliveryState);
        Assert.Equal(before, await Fingerprint());
    }

    [Fact]
    public async Task Pagination_projects_only_scoped_rows_and_preserves_membership_during_confirmation_and_reopen()
    {
        var store = await Close("first"); await Parent(store);
        await Close("second"); await Parent(store);
        await Close("foreign", "other");
        var first = await store.ReadClosingReportPageAsync("branch", Now, new(Limit: 1, LifecycleStatus: "Pending"));
        var secondRow = Assert.Single(first.Items); Assert.Equal("second", secondRow.RequestId);
        Assert.Equal("Queued", secondRow.LifecycleDelivery!.DeliveryState); Assert.NotNull(first.NextCursor);
        var context = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "worker", Now, Lease));
        Assert.Equal("first", context.RequestId); Assert.True(await store.ConfirmClosingLifecycleAsync(context, Now));
        await Close("appended");
        var reopened = new SqliteTableSessionCommandStore(_path);
        Assert.Empty((await reopened.ReadClosingReportPageAsync("branch", Now,
            new(Limit: 1, Cursor: first.NextCursor, LifecycleStatus: "Pending"))).Items);
        var delivered = Assert.Single((await reopened.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Delivered"))).Items);
        Assert.Equal("first", delivered.RequestId); Assert.Equal("Delivered", delivered.LifecycleDelivery!.DeliveryState);
        var fresh = await reopened.ReadClosingReportPageAsync("branch", Now, new(LifecycleStatus: "Pending"));
        Assert.Equal(new[] { "appended", "second" }, fresh.Items.Select(item => item.RequestId));
        Assert.All(fresh.Items, item => Assert.NotNull(item.LifecycleDelivery));
    }

    [Fact]
    public async Task Unknown_failure_is_sanitized_in_list_and_detail_without_exposing_worker_or_changing_storage()
    {
        var store = await Close(); await Parent(store);
        var lease = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "private-owner", Now, Lease));
        Assert.True(await store.RetryClosingLifecycleAsync(lease, Now, Lease, ClosingLifecycleFailure.CentralRejected));
        await Sql("UPDATE closing_report_context_outbox SET last_failure='PRIVATE-UNSUPPORTED-FAILURE';");
        var item = await Item(store, Now, "RetryScheduled");
        Assert.Null(item.LifecycleDelivery!.LastFailure); Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(item));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
