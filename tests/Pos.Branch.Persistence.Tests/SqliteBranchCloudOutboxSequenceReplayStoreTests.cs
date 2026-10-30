using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudOutboxSequenceReplayStoreTests
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 10, 30, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Requests_retained_sequence_without_erasing_acknowledgment()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 6, acknowledged: true);
        var store = new SqliteBranchCloudOutboxSequenceReplayStore(database.Path);

        var result = await store.RequestAsync(Request());

        Assert.Equal(BranchCloudOutboxSequenceReplayResult.Requested, result);
        Assert.Equal(1, await database.ReplayCountAsync());
        Assert.Equal(
            "2026-10-30T04:00:00.0000000+00:00",
            await database.ReadAcknowledgedAtAsync());
    }

    [Fact]
    public async Task Exact_recovery_retry_is_idempotent()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 6, acknowledged: true);
        var store = new SqliteBranchCloudOutboxSequenceReplayStore(database.Path);
        await store.RequestAsync(Request());

        var replay = await store.RequestAsync(Request());

        Assert.Equal(
            BranchCloudOutboxSequenceReplayResult.AlreadyRequested,
            replay);
        Assert.Equal(1, await database.ReplayCountAsync());
    }

    [Fact]
    public async Task Reused_recovery_id_with_different_data_fails_closed()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 5, acknowledged: true);
        await database.SeedAsync(sequence: 6, acknowledged: true);
        var store = new SqliteBranchCloudOutboxSequenceReplayStore(database.Path);
        await store.RequestAsync(Request());
        var conflict = new BranchCloudOutboxSequenceReplayRequest(
            "RECOVERY-01",
            "BRANCH-01",
            5,
            7,
            RequestedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RequestAsync(conflict));

        Assert.Equal(1, await database.ReplayCountAsync());
    }

    [Fact]
    public async Task Different_recovery_id_does_not_duplicate_active_sequence()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 6, acknowledged: true);
        var store = new SqliteBranchCloudOutboxSequenceReplayStore(database.Path);
        await store.RequestAsync(Request());
        var duplicate = new BranchCloudOutboxSequenceReplayRequest(
            "RECOVERY-02",
            "BRANCH-01",
            6,
            8,
            RequestedAt.AddSeconds(1));

        var result = await store.RequestAsync(duplicate);

        Assert.Equal(
            BranchCloudOutboxSequenceReplayResult.AlreadyRequested,
            result);
        Assert.Equal(1, await database.ReplayCountAsync());
    }

    [Fact]
    public async Task Missing_expected_sequence_returns_not_found_without_record()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        var store = new SqliteBranchCloudOutboxSequenceReplayStore(database.Path);

        var result = await store.RequestAsync(Request());

        Assert.Equal(
            BranchCloudOutboxSequenceReplayResult.ExpectedSequenceNotFound,
            result);
        Assert.Equal(0, await database.ReplayCountAsync());
    }

    [Fact]
    public async Task Missing_request_is_rejected()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        var store = new SqliteBranchCloudOutboxSequenceReplayStore(database.Path);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.RequestAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string databasePath)
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteBranchCloudOutboxSequenceReplayStore(databasePath));
    }

    [Fact]
    public async Task Version_20_upgrade_preserves_replay_intent_and_acknowledgment()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path)
            .RequestAsync(Request());
        await database.ExecuteAsync("""
            DROP TABLE cloud_sync_sequence_replay_leases;
            PRAGMA user_version = 20;
            """);

        await database.InitializeAsync();
        await database.InitializeAsync();
        await database.ExecuteAsync("""
            INSERT INTO cloud_sync_sequence_replay_leases (recovery_id)
            VALUES ('RECOVERY-01');
            """);

        Assert.Equal(1, await database.ReplayCountAsync());
        Assert.Equal(
            "2026-10-30T04:00:00.0000000+00:00",
            await database.ReadAcknowledgedAtAsync());
    }

    [Theory]
    [InlineData("retry_count", "-1")]
    [InlineData("lease_owner", "'WORKER-01'")]
    [InlineData("claimed_at", "'2026-10-30T05:00:00Z'")]
    [InlineData("lease_expires_at", "'2026-10-30T05:00:30Z'")]
    public async Task Replay_lease_rejects_invalid_state(string column, string value)
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path)
            .RequestAsync(Request());

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync(
            $"INSERT INTO cloud_sync_sequence_replay_leases (recovery_id, {column}) VALUES ('RECOVERY-01', {value});"));
    }

    [Theory]
    [InlineData("2026-10-30T05:00:00Z")]
    [InlineData("2026-10-30T04:59:59Z")]
    [InlineData("invalid")]
    public async Task Replay_lease_rejects_invalid_expiry(string expiry)
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path)
            .RequestAsync(Request());

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync($"""
            INSERT INTO cloud_sync_sequence_replay_leases (
                recovery_id, lease_owner, claimed_at, lease_expires_at)
            VALUES ('RECOVERY-01', 'WORKER-01', '2026-10-30T05:00:00Z', '{expiry}');
            """));
    }

    [Fact]
    public async Task Replay_lease_accepts_active_ownership_and_enforces_request_identity()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(sequence: 6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path)
            .RequestAsync(Request());
        await database.ExecuteAsync("""
            INSERT INTO cloud_sync_sequence_replay_leases (
                recovery_id, lease_owner, claimed_at, lease_expires_at)
            VALUES ('RECOVERY-01', 'WORKER-01', '2026-10-30T05:00:00Z',
                '2026-10-30T05:00:30Z');
            """);

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            INSERT INTO cloud_sync_sequence_replay_leases (recovery_id)
            VALUES ('RECOVERY-01');
            """));
        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            INSERT INTO cloud_sync_sequence_replay_leases (recovery_id)
            VALUES ('MISSING');
            """));
    }

    [Fact]
    public async Task Replay_claim_preserves_original_ack_and_reclaims_at_expiry()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path).RequestAsync(Request());
        var store = new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path);

        var lease = await store.ClaimNextAsync("BRANCH-01", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(30));
        Assert.NotNull(lease);
        Assert.Equal("RECOVERY-01", lease.RecoveryId);
        Assert.Equal(6, lease.BranchSequence);
        Assert.Equal("OUTBOX-6", lease.OutboxId);
        Assert.Equal(0, lease.RetryCount);
        Assert.Null(await store.ClaimNextAsync("BRANCH-01", "WORKER-02", RequestedAt.AddSeconds(29), TimeSpan.FromSeconds(30)));
        var reclaimed = await store.ClaimNextAsync("BRANCH-01", "WORKER-02", RequestedAt.AddSeconds(30), TimeSpan.FromSeconds(30));
        Assert.Equal("WORKER-02", reclaimed?.LeaseOwner);
        Assert.Equal("2026-10-30T04:00:00.0000000+00:00", await database.ReadAcknowledgedAtAsync());
    }

    [Fact]
    public async Task Replay_claim_does_not_skip_earliest_sequence_waiting_for_retry()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(5, acknowledged: true);
        await database.SeedAsync(6, acknowledged: true);
        var requests = new SqliteBranchCloudOutboxSequenceReplayStore(database.Path);
        await requests.RequestAsync(Request());
        await requests.RequestAsync(new("RECOVERY-02", "BRANCH-01", 5, 7, RequestedAt.AddSeconds(1)));
        await database.ExecuteAsync("""
            INSERT INTO cloud_sync_sequence_replay_leases (recovery_id, retry_count, next_retry_at)
            VALUES ('RECOVERY-02', 2, '2026-10-30T04:31:00Z');
            """);
        var store = new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path);

        Assert.Null(await store.ClaimNextAsync("BRANCH-01", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(30)));
        var lease = await store.ClaimNextAsync("BRANCH-01", "WORKER-01", RequestedAt.AddMinutes(1), TimeSpan.FromSeconds(30));
        Assert.Equal(5, lease?.BranchSequence);
        Assert.Equal(2, lease?.RetryCount);
    }

    [Fact]
    public async Task Replay_claim_excludes_completed_requests_and_other_branches()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path).RequestAsync(Request());
        var store = new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path);
        Assert.Null(await store.ClaimNextAsync("OTHER", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(30)));
        await database.ExecuteAsync("UPDATE cloud_sync_sequence_replay_requests SET completed_at = '2026-10-30T04:30:00Z';");
        Assert.Null(await store.ClaimNextAsync("BRANCH-01", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Concurrent_replay_claims_grant_one_owner()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path).RequestAsync(Request());
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(() =>
            new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path).ClaimNextAsync(
                "BRANCH-01", $"WORKER-{index}", RequestedAt, TimeSpan.FromSeconds(30)))));

        Assert.Single(results, result => result is not null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Replay_claim_rejects_invalid_duration_before_database_access(int seconds)
    {
        var store = new SqliteBranchCloudOutboxSequenceReplayClaimStore("missing-replay.db");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ClaimNextAsync(
            "BRANCH-01", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task Replay_renewal_extends_exact_token_and_rejects_stale_token()
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path).RequestAsync(Request());
        var claims = new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path);
        var lease = (await claims.ClaimNextAsync("BRANCH-01", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(30)))!;
        var store = new SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore(database.Path);

        var renewed = await store.RenewAsync(lease, RequestedAt.AddSeconds(10), TimeSpan.FromSeconds(30));
        Assert.Equal(RequestedAt.AddSeconds(40), renewed.LeaseExpiresAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenewAsync(
            lease, RequestedAt.AddSeconds(15), TimeSpan.FromSeconds(30)));
        Assert.Null(await claims.ClaimNextAsync("BRANCH-01", "WORKER-02", RequestedAt.AddSeconds(35), TimeSpan.FromSeconds(30)));
        Assert.Equal("2026-10-30T04:00:00.0000000+00:00", await database.ReadAcknowledgedAtAsync());
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("owner")]
    [InlineData("claimed")]
    [InlineData("missing")]
    public async Task Replay_renewal_rejects_changed_persisted_ownership(string change)
    {
        await using var database = new ReplayDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path).RequestAsync(Request());
        var lease = (await new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path)
            .ClaimNextAsync("BRANCH-01", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(30)))!;
        await database.ExecuteAsync(change switch
        {
            "completed" => "UPDATE cloud_sync_sequence_replay_requests SET completed_at = '2026-10-30T04:30:05Z';",
            "owner" => "UPDATE cloud_sync_sequence_replay_leases SET lease_owner = 'OTHER';",
            "claimed" => "UPDATE cloud_sync_sequence_replay_leases SET claimed_at = '2026-10-30T04:30:01Z';",
            _ => "DELETE FROM cloud_sync_sequence_replay_leases;"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore(database.Path)
                .RenewAsync(lease, RequestedAt.AddSeconds(10), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Replay_renewal_rejects_expiry_before_opening_database()
    {
        var lease = new BranchCloudOutboxSequenceReplayLease(
            "RECOVERY-01", "OUTBOX-6", "BRANCH-01", 6, "REQUEST-6",
            Pos.Domain.TableSessions.TableSessionId.From("SESSION-6"),
            Pos.Domain.TableSessions.TableSessionAction.SubmitOrder,
            3, RequestedAt, 0, "WORKER-01", RequestedAt, RequestedAt.AddSeconds(30));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore("missing-replay.db")
                .RenewAsync(lease, RequestedAt.AddSeconds(30), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Replay_completion_is_idempotent_and_preserves_first_evidence()
    {
        await using var database = new ReplayDatabase();
        var lease = await SeedClaimAsync(database);
        var store = new SqliteBranchCloudOutboxSequenceReplayCompletionStore(database.Path);

        var result = await store.CompleteAsync(new(lease, lease.OutboxId, 6, RequestedAt.AddSeconds(10)));
        var duplicate = await store.CompleteAsync(new(lease, lease.OutboxId, 6, RequestedAt.AddSeconds(15)));

        Assert.Equal(BranchCloudOutboxSequenceReplayCompletionResult.Completed, result);
        Assert.Equal(BranchCloudOutboxSequenceReplayCompletionResult.AlreadyCompleted, duplicate);
        Assert.Equal("2026-10-30T04:30:10.0000000+00:00", await database.TextAsync(
            "SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
        Assert.Null(await database.TextAsync("SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
        Assert.Equal("2026-10-30T04:00:00.0000000+00:00", await database.ReadAcknowledgedAtAsync());
        Assert.Null(await new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path)
            .ClaimNextAsync("BRANCH-01", "WORKER-02", RequestedAt.AddMinutes(1), TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("expiry")]
    [InlineData("missing")]
    public async Task Replay_completion_rejects_stale_ownership_without_completing(string change)
    {
        await using var database = new ReplayDatabase();
        var lease = await SeedClaimAsync(database);
        await database.ExecuteAsync(change switch
        {
            "owner" => "UPDATE cloud_sync_sequence_replay_leases SET lease_owner = 'OTHER';",
            "expiry" => "UPDATE cloud_sync_sequence_replay_leases SET lease_expires_at = '2026-10-30T04:30:40Z';",
            _ => "DELETE FROM cloud_sync_sequence_replay_leases;"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqliteBranchCloudOutboxSequenceReplayCompletionStore(database.Path)
                .CompleteAsync(new(lease, lease.OutboxId, 6, RequestedAt.AddSeconds(10))));
        Assert.Null(await database.TextAsync("SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
    }

    [Theory]
    [InlineData("OTHER-BRANCH", "RECOVERY-01", "OUTBOX-6", false)]
    [InlineData("BRANCH-01", "MISSING", "OUTBOX-6", false)]
    [InlineData("BRANCH-01", "RECOVERY-01", "OTHER-EVENT", true)]
    public async Task Replay_completion_checks_recovery_and_event_identity(
        string branch, string recovery, string outbox, bool conflict)
    {
        await using var database = new ReplayDatabase();
        var original = await SeedClaimAsync(database);
        var lease = new BranchCloudOutboxSequenceReplayLease(
            recovery, outbox, branch, 6, original.RequestId, original.TableSessionId,
            original.Action, original.TableVersion, original.CreatedAt, original.RetryCount,
            original.LeaseOwner, original.ClaimedAt, original.LeaseExpiresAt);
        var store = new SqliteBranchCloudOutboxSequenceReplayCompletionStore(database.Path);
        var completion = new BranchCloudOutboxSequenceReplayCompletion(lease, outbox, 6, RequestedAt.AddSeconds(10));

        if (conflict)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteAsync(completion));
        }
        else
        {
            Assert.Equal(BranchCloudOutboxSequenceReplayCompletionResult.NotFound,
                await store.CompleteAsync(completion));
        }
        Assert.Null(await database.TextAsync("SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
    }

    [Fact]
    public async Task Replay_completion_rolls_back_when_lease_release_fails()
    {
        await using var database = new ReplayDatabase();
        var lease = await SeedClaimAsync(database);
        await database.ExecuteAsync("""
            CREATE TRIGGER reject_replay_release BEFORE UPDATE OF lease_owner
            ON cloud_sync_sequence_replay_leases
            WHEN NEW.lease_owner IS NULL
            BEGIN SELECT RAISE(ABORT, 'simulated release failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() =>
            new SqliteBranchCloudOutboxSequenceReplayCompletionStore(database.Path)
                .CompleteAsync(new(lease, lease.OutboxId, 6, RequestedAt.AddSeconds(10))));

        Assert.Null(await database.TextAsync("SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
        Assert.Equal("WORKER-01", await database.TextAsync("SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
    }

    [Fact]
    public async Task Version_21_upgrade_preserves_recovery_and_lease_evidence()
    {
        await using var database = new ReplayDatabase();
        await SeedClaimAsync(database);
        await database.ExecuteAsync("""
            DROP TABLE cloud_sync_sequence_replay_failures;
            DROP INDEX ux_cloud_sync_replay_request_identity;
            PRAGMA user_version = 21;
            """);

        await database.InitializeAsync();
        await database.InitializeAsync();
        await database.ExecuteAsync(FailureInsert());

        Assert.Equal("22", await database.TextAsync("SELECT CAST(user_version AS TEXT) FROM pragma_user_version;"));
        Assert.Equal("WORKER-01", await database.TextAsync("SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
        Assert.Equal("2026-10-30T04:00:00.0000000+00:00", await database.ReadAcknowledgedAtAsync());
    }

    [Theory]
    [InlineData("retry_count_after", "0")]
    [InlineData("expected_branch_sequence", "6")]
    [InlineData("expected_branch_sequence", "0")]
    [InlineData("failed_at", "'2026-10-30T04:30:30Z'")]
    [InlineData("next_retry_at", "'2026-10-30T04:30:05Z'")]
    [InlineData("claimed_at", "'invalid'")]
    [InlineData("branch_id", "'OTHER'")]
    [InlineData("outbox_id", "'OTHER'")]
    [InlineData("recovery_id", "'MISSING'")]
    public async Task Replay_failure_schema_rejects_invalid_evidence(string column, string value)
    {
        await using var database = new ReplayDatabase();
        await SeedClaimAsync(database);
        await database.ExecuteAsync(FailureInsert());

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync(
            $"UPDATE cloud_sync_sequence_replay_failures SET {column} = {value};"));
    }

    [Fact]
    public async Task Replay_failure_schema_enforces_unique_failure_identity()
    {
        await using var database = new ReplayDatabase();
        await SeedClaimAsync(database);
        await database.ExecuteAsync(FailureInsert());

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync(FailureInsert()));
        await database.ExecuteAsync("DELETE FROM cloud_sync_sequence_replay_requests;");
        Assert.Null(await database.TextAsync("SELECT failure_id FROM cloud_sync_sequence_replay_failures;"));
    }

    private static string FailureInsert() => """
        INSERT INTO cloud_sync_sequence_replay_failures (
            failure_id, recovery_id, branch_id, outbox_id, branch_sequence,
            lease_owner, claimed_at, lease_expires_at, failed_at, next_retry_at,
            error_code, retry_count_after, expected_branch_sequence)
        VALUES ('FAILURE-01', 'RECOVERY-01', 'BRANCH-01', 'OUTBOX-6', 6,
            'WORKER-01', '2026-10-30T04:30:00Z', '2026-10-30T04:30:30Z',
            '2026-10-30T04:30:05Z', '2026-10-30T04:31:00Z',
            'BranchSequenceGap', 1, 5);
        """;

    private static async Task<BranchCloudOutboxSequenceReplayLease> SeedClaimAsync(ReplayDatabase database)
    {
        await database.InitializeAsync();
        await database.SeedAsync(6, acknowledged: true);
        await new SqliteBranchCloudOutboxSequenceReplayStore(database.Path).RequestAsync(Request());
        return (await new SqliteBranchCloudOutboxSequenceReplayClaimStore(database.Path)
            .ClaimNextAsync("BRANCH-01", "WORKER-01", RequestedAt, TimeSpan.FromSeconds(30)))!;
    }

    private static BranchCloudOutboxSequenceReplayRequest Request() =>
        new(
            "RECOVERY-01",
            "BRANCH-01",
            6,
            7,
            RequestedAt);

    private sealed class ReplayDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-sequence-replay-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task SeedAsync(long sequence, bool acknowledged)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cloud_sync_outbox (
                    outbox_id, branch_id, request_id, table_session_id,
                    action, table_version, created_at, processing_status,
                    retry_count, successfully_sent_at, branch_sequence)
                VALUES (
                    $outboxId, 'BRANCH-01', $requestId, $tableSessionId,
                    'SubmitOrder', 3,
                    '2026-10-30T03:30:00.0000000+00:00', 'Completed',
                    0, $acknowledgedAt, $sequence);
                """;
            command.Parameters.AddWithValue("$outboxId", $"OUTBOX-{sequence}");
            command.Parameters.AddWithValue("$requestId", $"REQUEST-{sequence}");
            command.Parameters.AddWithValue(
                "$tableSessionId",
                $"SESSION-{sequence}");
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue(
                "$acknowledgedAt",
                acknowledged
                    ? "2026-10-30T04:00:00.0000000+00:00"
                    : DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string?> TextAsync(string sql)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync() as string;
        }

        public async Task<int> ReplayCountAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM cloud_sync_sequence_replay_requests;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<string?> ReadAcknowledgedAtAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT successfully_sent_at
                FROM cloud_sync_outbox
                WHERE branch_id = 'BRANCH-01'
                  AND branch_sequence = 6;
                """;
            return await command.ExecuteScalarAsync() as string;
        }

        private async Task<SqliteConnection> OpenAsync()
        {
            Directory.CreateDirectory(_directory);
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
