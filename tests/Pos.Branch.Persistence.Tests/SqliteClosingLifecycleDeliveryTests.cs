using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteClosingLifecycleDeliveryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lifecycle-delivery-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(1);
    private static StoreLifecycleCommand Command(string request, StoreLifecycleAction action, decimal? amount = null,
        string shift = "first") => new(request, "store", shift, action, amount, new("employee", "device", Now, new(2026, 11, 22)));
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync(); return store;
    }
    private async Task<SqliteTableSessionCommandStore> Closed(string branch = "branch")
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync(branch, Command("open", StoreLifecycleAction.OpenStore, 500));
        await store.ExecuteStoreLifecycleAsync(branch, Command("close", StoreLifecycleAction.CloseStore, 490));
        return store;
    }
    private async Task AckParent(SqliteTableSessionCommandStore store, string branch = "branch")
    {
        var parent = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync(branch, "base", Now, Duration));
        Assert.True(await store.ConfirmClosingReportAsync(parent, Now));
    }
    private async Task Sql(string text)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Foreign Keys=True");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = text; await command.ExecuteNonQueryAsync();
    }
    private async Task<string?> Scalar(string text)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = text; return (await command.ExecuteScalarAsync())?.ToString();
    }
    [Fact]
    public async Task Context_is_durable_but_cannot_be_claimed_until_parent_acknowledgement()
    {
        var store = await Closed();
        var parentBytes = await Scalar("SELECT payload_json FROM closing_report_outbox;");
        Assert.Equal("1", await Scalar("SELECT count(*) FROM closing_report_context_outbox;"));
        Assert.Null(await store.ClaimClosingLifecycleAsync("branch", "context", Now, Duration));
        await AckParent(store);
        var pending = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now);
        Assert.Equal("Pending", pending.State);
        Assert.Equal(1, pending.LifecycleReports!.PendingCount);
        Assert.Equal(1, pending.ClosingReports.DeliveredCount);
        var lease = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "context", Now, Duration));
        Assert.Equal(parentBytes, lease.ParentPayloadJson);
        using var body = JsonDocument.Parse(lease.PayloadJson);
        Assert.Equal("lifecycle:" + lease.ReportId, body.RootElement.GetProperty("contextId").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("close", body.RootElement.GetProperty("context").GetProperty("RequestId").GetString());
        Assert.True(await store.ConfirmClosingLifecycleAsync(lease, Now));
        var complete = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now);
        Assert.Equal("Current", complete.State); Assert.Equal(1, complete.LifecycleReports!.DeliveredCount);
        Assert.Equal(parentBytes, await Scalar("SELECT payload_json FROM closing_report_outbox;"));
        Assert.Equal(Now, (await store.ReadClosingReportDetailAsync("branch", "close", Now))!.LifecycleDelivery!.DeliveredAt);
    }
    [Fact]
    public async Task Retry_expiry_reopen_and_fenced_confirmation_preserve_exact_packets()
    {
        var store = await Closed(); await AckParent(store);
        var first = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "one", Now, Duration));
        Assert.Null(await store.ClaimClosingLifecycleAsync("branch", "two", Now, Duration));
        Assert.False(await store.ConfirmClosingLifecycleAsync(first with { BranchId = "foreign" }, Now));
        Assert.False(await store.ConfirmClosingLifecycleAsync(first with { LeaseOwner = "foreign" }, Now));
        var reopened = await Store();
        var second = Assert.IsType<ClosingLifecycleLease>(await reopened.ClaimClosingLifecycleAsync("branch", "two", Now.AddMinutes(1), Duration));
        Assert.Equal(first.ContextId, second.ContextId); Assert.Equal(first.PayloadJson, second.PayloadJson);
        Assert.Equal(first.ParentPayloadJson, second.ParentPayloadJson); Assert.Equal(2, second.AttemptCount);
        Assert.False(await store.ConfirmClosingLifecycleAsync(first, Now.AddMinutes(1)));
        Assert.False(await store.RetryClosingLifecycleAsync(first, Now.AddMinutes(1), Duration, ClosingLifecycleFailure.ParentUnavailable));
        Assert.True(await store.RetryClosingLifecycleAsync(second, Now.AddMinutes(1), Duration, ClosingLifecycleFailure.ParentUnavailable));
        Assert.Null(await store.ClaimClosingLifecycleAsync("branch", "three", Now.AddSeconds(119), Duration));
        var third = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "three", Now.AddMinutes(2), Duration));
        Assert.Equal(first.PayloadJson, third.PayloadJson); Assert.Equal(3, third.AttemptCount);
        Assert.True(await store.ConfirmClosingLifecycleAsync(third, Now.AddMinutes(2)));
        Assert.False(await store.ConfirmClosingLifecycleAsync(third, Now.AddMinutes(2)));
    }
    [Fact]
    public async Task Scoped_workers_and_competing_connections_cannot_claim_the_same_context()
    {
        var store = await Closed(); await Closed("foreign"); await AckParent(store); await AckParent(store, "foreign");
        var other = await Store();
        var claims = await Task.WhenAll(store.ClaimClosingLifecycleAsync("branch", "one", Now, Duration), other.ClaimClosingLifecycleAsync("branch", "two", Now, Duration));
        Assert.Single(claims, item => item is not null);
        Assert.NotNull(await store.ClaimClosingLifecycleAsync("foreign", "one", Now, Duration));
        Assert.Null(await store.ClaimClosingLifecycleAsync("missing", "one", Now, Duration));
        var foreign = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("missing", Now);
        Assert.Equal("NoHistory", foreign.State); Assert.Equal(0, foreign.LifecycleReports!.TotalCount);
    }
    [Fact]
    public async Task Migration_34_backfills_only_stored_context_and_preserves_parent_and_snapshot_bytes()
    {
        var store = await Closed(); await AckParent(store);
        var parent = await Scalar("SELECT payload_json FROM closing_report_outbox;");
        var context = await Scalar("SELECT context_json FROM closing_report_lifecycle_snapshots;");
        await Sql(global::Pos.Branch.Persistence.Tests.LegacySchemaFixture.WithoutKitchenReprints("DROP TABLE closing_report_context_outbox; PRAGMA user_version=34;"));
        store = await Store(); await Store();
        Assert.Equal("36", await Scalar("PRAGMA user_version;"));
        Assert.Equal("1", await Scalar("SELECT count(*) FROM closing_report_context_outbox;"));
        Assert.Equal(parent, await Scalar("SELECT payload_json FROM closing_report_outbox;"));
        Assert.Equal(context, await Scalar("SELECT context_json FROM closing_report_lifecycle_snapshots;"));
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", Now)).DeliveredCount);
        Assert.NotNull(await store.ClaimClosingLifecycleAsync("branch", "context", Now, Duration));
    }
    [Fact]
    public async Task Orphan_context_job_and_changed_context_identity_fail_closed()
    {
        var store = await Closed();
        await Sql("UPDATE closing_report_context_outbox SET context_id='lifecycle:wrong-parent';");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadClosingReportDetailAsync("branch", "close", Now));
        await Sql("PRAGMA foreign_keys=OFF; DELETE FROM closing_report_lifecycle_snapshots;");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadClosingReportDetailAsync("branch", "close", Now));
    }
    [Fact]
    public async Task Migration_34_pending_parent_remains_a_prerequisite_for_context_claim()
    {
        var store = await Closed();
        await Sql(global::Pos.Branch.Persistence.Tests.LegacySchemaFixture.WithoutKitchenReprints("DROP TABLE closing_report_context_outbox; PRAGMA user_version=34;"));
        store = await Store();
        Assert.Null(await store.ClaimClosingLifecycleAsync("branch", "context", Now, Duration));
        await AckParent(store);
        Assert.NotNull(await store.ClaimClosingLifecycleAsync("branch", "context", Now, Duration));
    }
    [Fact]
    public async Task Missing_context_job_and_malformed_context_times_make_entire_overview_unavailable()
    {
        await Closed();
        await Sql("DELETE FROM closing_report_context_outbox;");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now));
        var store = await Store();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadClosingReportDetailAsync("branch", "close", Now));
        await Sql(global::Pos.Branch.Persistence.Tests.LegacySchemaFixture.WithoutKitchenReprints("PRAGMA user_version=34;")); await Store();
        await Sql("UPDATE closing_report_context_outbox SET available_at='private-corrupt-time';");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now));
    }
    [Fact]
    public async Task Context_enqueue_failure_rolls_back_all_close_records_then_same_request_retries()
    {
        var store = await Store(); await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await Sql("CREATE TRIGGER fail_context_job BEFORE INSERT ON closing_report_context_outbox BEGIN SELECT RAISE(ABORT,'context delivery unavailable'); END;");
        var close = Command("close", StoreLifecycleAction.CloseStore, 500);
        await Assert.ThrowsAsync<SqliteException>(() => store.ExecuteStoreLifecycleAsync("branch", close));
        foreach (var table in new[] { "closing_report_outbox", "closing_report_lifecycle_snapshots", "closing_report_context_outbox", "store_lifecycle_cash_snapshots" })
        {
            Assert.Equal("0", await Scalar($"SELECT count(*) FROM {table};"));
        }
        Assert.False((await store.ReadCurrentStoreLifecycleAsync("branch"))!.IsClosed);
        await Sql("DROP TRIGGER fail_context_job;"); await store.ExecuteStoreLifecycleAsync("branch", close);
        Assert.Equal("1", await Scalar("SELECT count(*) FROM closing_report_context_outbox;"));
    }
    [Fact]
    public async Task Detail_sanitizes_unknown_failures_and_does_not_mutate_delivery_or_local_snapshot()
    {
        var store = await Closed(); await AckParent(store);
        var lease = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("branch", "private-worker", Now, Duration));
        Assert.True(await store.RetryClosingLifecycleAsync(lease, Now, Duration, ClosingLifecycleFailure.ParentUnavailable));
        await Sql("UPDATE closing_report_context_outbox SET last_failure='PRIVATE-RAW-FAILURE';");
        var before = await Scalar("SELECT payload_json || context_id || attempt_count || available_at FROM closing_report_context_outbox;");
        var detail = (await store.ReadClosingReportDetailAsync("branch", "close", Now))!;
        Assert.Equal("RetryScheduled", detail.LifecycleDelivery!.DeliveryState); Assert.Null(detail.LifecycleDelivery.LastFailure);
        var json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("PRIVATE-RAW-FAILURE", json); Assert.DoesNotContain("private-worker", json); Assert.DoesNotContain(lease.LeaseId, json);
        Assert.Equal(before, await Scalar("SELECT payload_json || context_id || attempt_count || available_at FROM closing_report_context_outbox;"));
    }
    [Fact]
    public async Task Context_acknowledgement_participates_in_exact_latest_confirmation_and_foreign_corruption_is_ignored()
    {
        var store = await Closed(); await Closed("foreign"); await AckParent(store);
        var lease = (await store.ClaimClosingLifecycleAsync("branch", "context", Now, Duration))!;
        var later = Now.AddTicks(7);
        Assert.True(await store.ConfirmClosingLifecycleAsync(lease, later));
        await Sql("UPDATE closing_report_context_outbox SET created_at='private-corrupt' WHERE branch_id='foreign';");
        var snapshot = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", later);
        Assert.Equal("Current", snapshot.State); Assert.Equal(later, snapshot.LastConfirmedAt);
    }
    public static IEnumerable<object[]> SourceMatrix =>
        from tables in new[] { false, true }
        from reports in new[] { false, true }
        from contexts in new[] { false, true }
        from replay in new[] { false, true }
        select new object[] { tables, reports, contexts, replay };
    [Theory]
    [MemberData(nameof(SourceMatrix))]
    public async Task Four_sources_are_pending_until_every_recorded_channel_is_confirmed(bool tablePending, bool reportPending,
        bool contextPending, bool replayPending)
    {
        await Closed();
        await Sql($"""
            INSERT INTO cloud_sync_outbox(outbox_id,branch_id,branch_sequence,request_id,table_session_id,action,table_version,created_at,processing_status,successfully_sent_at)
            VALUES('event','branch',1,'event','table','OpenTable',1,'2026-11-22T03:00:00Z','Pending',{(tablePending ? "NULL" : "'2026-11-22T03:00:00Z'")});
            INSERT INTO cloud_sync_sequence_replay_requests(recovery_id,branch_id,expected_branch_sequence,received_branch_sequence,expected_outbox_id,requested_at,completed_at)
            VALUES('replay','branch',1,2,'event','2026-11-22T03:00:00Z',{(replayPending ? "NULL" : "'2026-11-22T03:00:00Z'")});
            UPDATE closing_report_outbox SET delivered_at={(reportPending ? "NULL" : "'2026-11-22T03:00:00Z'")};
            UPDATE closing_report_context_outbox SET delivered_at={(contextPending ? "NULL" : "'2026-11-22T03:00:00Z'")};
            """);
        var snapshot = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now);
        Assert.Equal(tablePending || reportPending || contextPending || replayPending ? "Pending" : "Current", snapshot.State);
        Assert.Equal(contextPending ? 1 : 0, snapshot.LifecycleReports!.PendingCount);
        Assert.Equal(reportPending ? 1 : 0, snapshot.ClosingReports.PendingCount);
        Assert.Equal(tablePending ? 1 : 0, snapshot.TableEvents.PendingCount);
        Assert.Equal(replayPending ? 1 : 0, snapshot.ReplayPendingCount);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
