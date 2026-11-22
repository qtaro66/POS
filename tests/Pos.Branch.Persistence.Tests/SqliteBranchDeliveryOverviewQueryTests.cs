using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchDeliveryOverviewQueryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"delivery-overview-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 21);
    private long _sequence;
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }
    private SqliteBranchDeliveryOverviewQuery Query() => new(_path);
    private async Task Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Foreign Keys=True");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) { command.Parameters.AddWithValue(name, value ?? DBNull.Value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Table(string id, string branch = "branch", string status = "Pending", string? ack = null, string? created = null,
        string? lease = null, string? delay = null)
    {
        var sequence = ++_sequence;
        await Execute("""
            INSERT INTO cloud_sync_outbox
                (outbox_id,branch_id,request_id,table_session_id,action,table_version,created_at,processing_status,
                 branch_sequence,successfully_sent_at,lease_owner,claimed_at,lease_expires_at,next_retry_at)
            VALUES ($id,$branch,$id,$id,'OpenTable',1,$created,$status,$sequence,$ack,$owner,$claimed,$lease,$delay);
            """, ("$id", id), ("$branch", branch), ("$created", created ?? Now.ToString("O")), ("$status", status),
            ("$sequence", sequence), ("$ack", ack), ("$owner", lease is null ? null : "private-worker"),
            ("$claimed", lease is null ? null : Now.AddMinutes(-2).ToString("O")), ("$lease", lease), ("$delay", delay));
        return id;
    }
    private async Task Report(string branch = "branch", string id = "report", DateTimeOffset? occurred = null)
    {
        var store = await Store();
        var command = new StoreLifecycleCommand("open-" + id, "store-" + id, "shift-" + id, StoreLifecycleAction.OpenStore, 500,
            new("employee", "device", occurred ?? Now, Day));
        await store.ExecuteStoreLifecycleAsync(branch, command);
        await store.ExecuteStoreLifecycleAsync(branch, command with { RequestId = id, Action = StoreLifecycleAction.CloseStore, CashAmount = 490 });
    }
    private async Task Replay(string id = "replay", string branch = "branch", string eventId = "event", string? completed = null,
        string? requested = null)
    {
        await Execute("""
            INSERT INTO cloud_sync_sequence_replay_requests
                (recovery_id,branch_id,expected_branch_sequence,received_branch_sequence,expected_outbox_id,requested_at,completed_at)
            SELECT $id,$branch,branch_sequence,branch_sequence+1,outbox_id,$requested,$completed
            FROM cloud_sync_outbox WHERE outbox_id=$event AND branch_id=$branch;
            """, ("$id", id), ("$branch", branch), ("$event", eventId), ("$requested", requested ?? Now.ToString("O")), ("$completed", completed));
    }

    public static IEnumerable<object[]> SourceStates =>
        from table in Enumerable.Range(0, 3)
        from report in Enumerable.Range(0, 3)
        from replay in Enumerable.Range(0, 3)
        select new object[] { table, report, replay };

    [Theory]
    [MemberData(nameof(SourceStates))]
    public async Task Original_source_matrix_includes_new_pending_lifecycle_jobs(int table, int report, int replay)
    {
        var store = await Store();
        // A replay has a retained original event by its foreign key, even when the table channel was otherwise empty.
        if (table > 0 || replay > 0) { await Table("event", ack: table == 1 ? null : Now.ToString("O")); }
        if (report > 0)
        {
            await Report();
            if (report == 2)
            {
                var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "worker", Now, TimeSpan.FromMinutes(1)));
                Assert.True(await store.ConfirmClosingReportAsync(lease, Now));
            }
        }
        if (replay > 0) { await Replay(completed: replay == 2 ? Now.ToString("O") : null); }
        var snapshot = await Query().ReadAsync("branch", Now);
        var pending = table == 1 || report > 0 || replay == 1;
        var history = table == 2 || report == 2 || replay > 0;
        Assert.Equal(pending ? "Pending" : history ? "Current" : "NoHistory", snapshot.State);
        Assert.Equal(table == 1 ? 1 : 0, snapshot.TableEvents.PendingCount);
        Assert.Equal(report == 1 ? 1 : 0, snapshot.ClosingReports.PendingCount);
        Assert.Equal(report > 0 ? 1 : 0, snapshot.LifecycleReports!.PendingCount);
        Assert.Equal(replay == 1 ? 1 : 0, snapshot.ReplayPendingCount);
        Assert.Equal(replay == 2 ? 1 : 0, snapshot.ReplayCompletedCount);
        Assert.Equal(Now, snapshot.AsOf);
        Assert.Equal("branch", snapshot.BranchId);
        foreach (var channel in new[] { snapshot.TableEvents, snapshot.ClosingReports, snapshot.LifecycleReports! })
        {
            Assert.Equal(channel.TotalCount, channel.PendingCount + channel.DeliveredCount);
            Assert.Equal(channel.DeliveredCount > 0, channel.LastConfirmedAt is not null);
            Assert.Equal(channel.PendingCount > 0, channel.OldestPendingAt is not null);
        }
    }

    [Fact]
    public async Task Report_claim_retry_expiry_confirmation_and_reopen_are_observed_without_queue_mutation()
    {
        await Report();
        var store = await Store();
        var first = await Query().ReadAsync("branch", Now);
        Assert.Equal(1, first.ClosingReports.PendingCount);
        var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "private-worker", Now, TimeSpan.FromMinutes(1)));
        Assert.Equal(1, (await Query().ReadAsync("branch", Now)).ClosingReports.InFlightCount);
        Assert.Equal(1, (await Query().ReadAsync("branch", Now.AddMinutes(1))).ClosingReports.ExpiredLeaseCount);
        lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "replacement", Now.AddMinutes(1), TimeSpan.FromMinutes(1)));
        Assert.True(await store.RetryClosingReportAsync(lease, Now.AddMinutes(1), TimeSpan.FromMinutes(2), ClosingReportDeliveryFailure.AcknowledgmentUnavailable));
        var delayed = await Query().ReadAsync("branch", Now.AddMinutes(1));
        Assert.Equal(1, delayed.ClosingReports.DelayedCount);
        Assert.Equal(0, delayed.ClosingReports.InFlightCount);
        Assert.Null(delayed.LastConfirmedAt);
        lease = Assert.IsType<ClosingReportLease>(await (await Store()).ClaimClosingReportAsync("branch", "reopened", Now.AddMinutes(3), TimeSpan.FromMinutes(1)));
        Assert.True(await store.ConfirmClosingReportAsync(lease, Now.AddMinutes(3)));
        var before = await Dump();
        var confirmed = await new SqliteBranchDeliveryOverviewQuery(_path).ReadAsync("branch", Now.AddMinutes(4));
        Assert.Equal("Pending", confirmed.State);
        Assert.Equal(1, confirmed.LifecycleReports!.PendingCount);
        Assert.Equal(1, confirmed.ClosingReports.DeliveredCount);
        Assert.Equal(Now.AddMinutes(3), confirmed.LastConfirmedAt);
        Assert.Equal(before, await Dump());
    }

    [Fact]
    public async Task Exact_seven_fraction_extrema_include_replay_ack_and_ignore_foreign_history_and_corruption()
    {
        await Store();
        await Table("event", ack: "2026-11-21T03:00:00Z");
        await Table("fraction", ack: "2026-11-21T03:00:00.0000001+00:00");
        await Report(occurred: Now.AddHours(-1));
        await Execute("UPDATE closing_report_outbox SET delivered_at='2026-11-21T03:00:00.0000002Z' WHERE branch_id='branch';");
        await Replay(completed: "2026-11-21T03:00:00.0000003+00:00");
        await Table("foreign", "foreign", ack: "not-a-timestamp", created: "also-invalid");
        var result = await Query().ReadAsync("branch", Now.AddSeconds(1));
        Assert.Equal(Now.AddTicks(1), result.TableEvents.LastConfirmedAt);
        Assert.Equal(Now.AddTicks(2), result.ClosingReports.LastConfirmedAt);
        Assert.Equal(Now.AddTicks(3), result.ReplayLastConfirmedAt);
        Assert.Equal(Now.AddTicks(3), result.LastConfirmedAt);
        Assert.Equal("NoHistory", (await Query().ReadAsync("missing", Now)).State);
        Assert.Equal(0, (await Query().ReadAsync("missing", Now)).TableEvents.TotalCount);
    }

    [Fact]
    public async Task Pending_age_uses_oldest_source_creation_and_fixed_branch_parameters()
    {
        await Store();
        await Table("later", created: Now.ToString("O"));
        await Table("older", created: Now.AddHours(-2).ToString("O"));
        await Table("foreign", "foreign", created: Now.AddDays(-5).ToString("O"));
        await Report(occurred: Now.AddHours(-1));
        await Replay(eventId: "later", requested: Now.AddHours(-3).ToString("O"));
        var result = await Query().ReadAsync("branch", Now);
        Assert.Equal(Now.AddHours(-2), result.TableEvents.OldestPendingAt);
        Assert.Equal(Now.AddHours(-1), result.ClosingReports.OldestPendingAt);
        Assert.Equal(Now.AddHours(-3), result.ReplayOldestPendingAt);
        Assert.Equal("NoHistory", (await Query().ReadAsync("branch' OR 1=1 --", Now)).State);
    }

    [Fact]
    public async Task Unknown_statuses_are_counted_without_exposing_errors_or_treating_completed_without_ack_as_success()
    {
        await Store();
        await Table("unknown", status: "private-token-error");
        await Table("false-completed", status: "Completed");
        await Table("ack-authority", status: "private-old-status", ack: Now.ToString("O"));
        await Report();
        await Execute("UPDATE closing_report_outbox SET last_failure='private-url-secret',available_at=$future WHERE branch_id='branch';", ("$future", Now.AddMinutes(1).ToString("O")));
        var result = await Query().ReadAsync("branch", Now);
        Assert.Equal("Pending", result.State);
        Assert.Equal(2, result.TableEvents.UnknownCount);
        Assert.Equal(1, result.TableEvents.DeliveredCount);
        Assert.Equal(1, result.ClosingReports.UnknownCount);
        Assert.Equal(1, result.ClosingReports.DelayedCount);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("worker", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Table_pending_substates_are_disjoint_and_use_full_timestamp_precision()
    {
        await Store();
        await Table("live", lease: Now.AddTicks(1).ToString("O"), delay: Now.AddMinutes(1).ToString("O"));
        await Table("expired", lease: Now.ToString("O"));
        await Table("delayed", status: "Failed", delay: Now.AddTicks(1).ToString("O"));
        await Table("ready", status: "Failed", delay: Now.ToString("O"));
        var result = await Query().ReadAsync("branch", Now);
        Assert.Equal(4, result.TableEvents.PendingCount);
        Assert.Equal(1, result.TableEvents.InFlightCount);
        Assert.Equal(1, result.TableEvents.ExpiredLeaseCount);
        Assert.Equal(1, result.TableEvents.DelayedCount);
    }

    [Theory]
    [InlineData("cloud_sync_outbox", "created_at", "bad")]
    [InlineData("cloud_sync_outbox", "successfully_sent_at", "2026-11-21T03:00:00+07:00")]
    [InlineData("closing_report_outbox", "created_at", "2026-02-30T03:00:00Z")]
    [InlineData("closing_report_outbox", "delivered_at", "2026-11-21 03:00:00Z")]
    [InlineData("cloud_sync_sequence_replay_requests", "requested_at", "bad")]
    [InlineData("cloud_sync_sequence_replay_requests", "completed_at", "bad")]
    public async Task Corrupt_or_non_utc_timestamp_fails_closed_instead_of_partial_current(string table, string column, string value)
    {
        await Store();
        await Table("event", ack: Now.ToString("O"));
        await Report();
        await Replay(completed: Now.ToString("O"));
        await Execute($"UPDATE {table} SET {column}=$value WHERE branch_id='branch';", ("$value", value));
        await Assert.ThrowsAsync<InvalidDataException>(() => Query().ReadAsync("branch", Now));
    }

    [Theory]
    [InlineData("cloud_sync_outbox")]
    [InlineData("closing_report_outbox")]
    [InlineData("cloud_sync_sequence_replay_requests")]
    public async Task Missing_source_does_not_fall_back_to_partial_observations(string table)
    {
        await Store();
        await Execute($"DROP TABLE {table};");
        await Assert.ThrowsAsync<SqliteException>(() => Query().ReadAsync("branch", Now));
    }

    [Fact]
    public async Task Missing_database_is_not_created_and_cancellation_and_non_utc_reads_are_rejected()
    {
        await Assert.ThrowsAsync<SqliteException>(() => Query().ReadAsync("branch", Now));
        Assert.False(File.Exists(_path));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Query().ReadAsync("branch", Now, cancelled.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => Query().ReadAsync("branch", Now.ToOffset(TimeSpan.FromHours(7))));
        await Assert.ThrowsAsync<ArgumentException>(() => Query().ReadAsync(" branch", Now));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task Single_read_transaction_never_combines_different_atomic_writer_generations()
    {
        await Store();
        await Table("event");
        await Report();
        var writer = Task.Run(async () =>
        {
            for (var index = 0; index < 20; index++)
            {
                await Execute("""
                    BEGIN IMMEDIATE;
                    UPDATE cloud_sync_outbox SET successfully_sent_at=$ack WHERE branch_id='branch';
                    UPDATE closing_report_outbox SET delivered_at=$ack WHERE branch_id='branch';
                    COMMIT;
                    """, ("$ack", index % 2 == 0 ? Now.ToString("O") : null));
            }
        });
        for (var index = 0; index < 20; index++)
        {
            var result = await Query().ReadAsync("branch", Now);
            Assert.Equal(result.TableEvents.DeliveredCount, result.ClosingReports.DeliveredCount);
            Assert.Equal(result.TableEvents.PendingCount, result.ClosingReports.PendingCount);
        }
        await writer;
    }

    private async Task<string> Dump()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json_group_array(json_object('request',request_id,'payload',payload_json,'lease',lease_id,'attempts',attempt_count,'delivered',delivered_at,'available',available_at,'failure',last_failure)) FROM closing_report_outbox;";
        return (string)(await command.ExecuteScalarAsync())!;
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
