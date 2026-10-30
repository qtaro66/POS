using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudOutboxDeliveryFailureStoreTests
{
    private static readonly DateTimeOffset ClaimedAt =
        new(2026, 10, 24, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Records_failure_increments_retry_and_releases_lease_atomically()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);

        var result = await store.RecordAsync(Failure(lease));

        Assert.Equal(3, result.RetryCount);
        Assert.False(result.AlreadyRecorded);
        var row = await database.ReadOutboxAsync();
        Assert.Equal("Failed", row.Status);
        Assert.Equal(3, row.RetryCount);
        Assert.Equal("2026-10-24T13:01:10.0000000+00:00", row.NextRetryAt);
        Assert.Equal("CLOUD-UNAVAILABLE", row.LastError);
        Assert.Null(row.LeaseOwner);
        Assert.Null(row.ClaimedAt);
        Assert.Null(row.LeaseExpiresAt);
        Assert.Equal(1, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Repeating_same_failure_returns_original_result_without_increment()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        var failure = Failure(lease);
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);
        await store.RecordAsync(failure);

        var replay = await store.RecordAsync(failure);

        Assert.Equal(3, replay.RetryCount);
        Assert.True(replay.AlreadyRecorded);
        Assert.Equal(3, (await database.ReadOutboxAsync()).RetryCount);
        Assert.Equal(1, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Records_expected_sequence_for_gap_recovery()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync(sequence: 7);
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);

        await store.RecordAsync(Failure(lease, expectedBranchSequence: 6));

        Assert.Equal(6, await database.ReadExpectedSequenceAsync());
    }

    [Fact]
    public async Task Reusing_failure_id_with_different_expected_sequence_is_rejected()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync(sequence: 7);
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);
        await store.RecordAsync(Failure(lease, expectedBranchSequence: 6));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordAsync(Failure(lease, expectedBranchSequence: 5)));

        Assert.Equal(1, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Reusing_failure_id_with_different_result_is_rejected()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);
        await store.RecordAsync(Failure(lease));
        var conflict = new BranchCloudOutboxDeliveryFailure(
            "FAILURE-01",
            lease,
            ClaimedAt.AddSeconds(10),
            ClaimedAt.AddMinutes(2),
            "DIFFERENT-ERROR");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordAsync(conflict));

        Assert.Equal(3, (await database.ReadOutboxAsync()).RetryCount);
        Assert.Equal(1, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Stale_expiry_token_is_rejected_without_failure_record()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);
        var failedAt = lease.LeaseExpiresAt;
        var invalid = new BranchCloudOutboxDeliveryFailure(
            "FAILURE-01",
            CopyLease(lease, leaseExpiresAt: failedAt.AddSeconds(1)),
            failedAt,
            failedAt.AddMinutes(1),
            "CLOUD-UNAVAILABLE");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordAsync(invalid));

        Assert.Equal(2, (await database.ReadOutboxAsync()).RetryCount);
        Assert.Equal(0, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Replaced_owner_is_rejected_without_failure_record()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);
        var stale = CopyLease(lease, leaseOwner: "OTHER-WORKER");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordAsync(Failure(stale)));

        Assert.Equal("WORKER-01", (await database.ReadOutboxAsync()).LeaseOwner);
        Assert.Equal(0, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Confirmed_event_is_rejected_without_failure_record()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        await database.ConfirmAsync();
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordAsync(Failure(lease)));

        Assert.Equal(0, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Concurrent_duplicate_failure_increments_retry_once()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        var failure = Failure(lease);
        var first = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);
        var second = new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path);

        var results = await Task.WhenAll(
            first.RecordAsync(failure),
            second.RecordAsync(failure));

        Assert.Single(results, result => !result.AlreadyRecorded);
        Assert.Single(results, result => result.AlreadyRecorded);
        Assert.All(results, result => Assert.Equal(3, result.RetryCount));
        Assert.Equal(3, (await database.ReadOutboxAsync()).RetryCount);
        Assert.Equal(1, await database.FailureCountAsync());
    }

    [Fact]
    public async Task Retry_schedule_blocks_later_sequence_then_reclaims_first()
    {
        await using var database = new FailureDatabase();
        var lease = await database.CreateLeaseAsync();
        await database.SeedLaterAsync();
        await new SqliteBranchCloudOutboxDeliveryFailureStore(database.Path)
            .RecordAsync(Failure(lease));
        var claimStore = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var early = await claimStore.ClaimNextAsync(
            "BRANCH-01",
            "WORKER-02",
            ClaimedAt.AddMinutes(1),
            TimeSpan.FromSeconds(30));
        var due = await claimStore.ClaimNextAsync(
            "BRANCH-01",
            "WORKER-02",
            ClaimedAt.AddMinutes(1).AddSeconds(10),
            TimeSpan.FromSeconds(30));

        Assert.Null(early);
        Assert.NotNull(due);
        Assert.Equal("OUTBOX-01", due.OutboxId);
        Assert.Equal(1, due.BranchSequence);
    }

    [Fact]
    public async Task Missing_failure_is_rejected()
    {
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore("missing.db");

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.RecordAsync(null!));
    }

    [Fact]
    public async Task Missing_database_is_not_created()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-failure-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var store = new SqliteBranchCloudOutboxDeliveryFailureStore(path);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.RecordAsync(Failure(LeaseFixture())));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string databasePath)
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteBranchCloudOutboxDeliveryFailureStore(databasePath));
    }

    private static BranchCloudOutboxDeliveryFailure Failure(
        BranchCloudOutboxLease lease,
        long? expectedBranchSequence = null) =>
        new(
            "FAILURE-01",
            lease,
            ClaimedAt.AddSeconds(10),
            ClaimedAt.AddMinutes(1).AddSeconds(10),
            expectedBranchSequence is null
                ? "CLOUD-UNAVAILABLE"
                : "BranchSequenceGap",
            expectedBranchSequence);

    private static BranchCloudOutboxLease CopyLease(
        BranchCloudOutboxLease lease,
        string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null) =>
        new(
            lease.OutboxId,
            lease.BranchId,
            lease.BranchSequence,
            lease.RequestId,
            lease.TableSessionId,
            lease.Action,
            lease.TableVersion,
            lease.CreatedAt,
            lease.RetryCount,
            leaseOwner ?? lease.LeaseOwner,
            lease.ClaimedAt,
            leaseExpiresAt ?? lease.LeaseExpiresAt);

    private static BranchCloudOutboxLease LeaseFixture() =>
        new(
            "OUTBOX-01",
            "BRANCH-01",
            1,
            "REQUEST-01",
            Pos.Domain.TableSessions.TableSessionId.From("SESSION-01"),
            Pos.Domain.TableSessions.TableSessionAction.SubmitOrder,
            3,
            ClaimedAt.AddMinutes(-1),
            2,
            "WORKER-01",
            ClaimedAt,
            ClaimedAt.AddSeconds(30));

    private sealed class FailureDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-failure-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task<BranchCloudOutboxLease> CreateLeaseAsync(long sequence = 1)
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
            await SeedAsync("OUTBOX-01", sequence);
            return (await new SqliteBranchCloudOutboxClaimStore(Path).ClaimNextAsync(
                "BRANCH-01",
                "WORKER-01",
                ClaimedAt,
                TimeSpan.FromSeconds(30)))!;
        }

        public Task SeedLaterAsync() => SeedAsync("OUTBOX-02", 2);

        public async Task ConfirmAsync()
        {
            await ExecuteAsync("""
                UPDATE cloud_sync_outbox
                SET successfully_sent_at =
                    '2026-10-24T13:00:05.0000000+00:00'
                WHERE outbox_id = 'OUTBOX-01';
                """);
        }

        public async Task<OutboxRow> ReadOutboxAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT processing_status, retry_count, next_retry_at,
                       last_error, lease_owner, claimed_at, lease_expires_at
                FROM cloud_sync_outbox
                WHERE outbox_id = 'OUTBOX-01';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new OutboxRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6));
        }

        public async Task<int> FailureCountAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM cloud_sync_delivery_failures;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task<long?> ReadExpectedSequenceAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT expected_branch_sequence
                FROM cloud_sync_delivery_failures
                WHERE failure_id = 'FAILURE-01';
                """;
            var value = await command.ExecuteScalarAsync();
            return value is long sequence ? sequence : null;
        }

        private async Task SeedAsync(string outboxId, long sequence)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cloud_sync_outbox (
                    outbox_id, branch_id, request_id, table_session_id,
                    action, table_version, created_at, processing_status,
                    retry_count, branch_sequence)
                VALUES (
                    $outboxId, 'BRANCH-01', $requestId, $tableSessionId,
                    'SubmitOrder', 3,
                    '2026-10-24T12:00:00.0000000+00:00', 'Pending',
                    2, $sequence);
                """;
            command.Parameters.AddWithValue("$outboxId", outboxId);
            command.Parameters.AddWithValue("$requestId", $"REQUEST-{outboxId}");
            command.Parameters.AddWithValue(
                "$tableSessionId",
                $"SESSION-{outboxId}");
            command.Parameters.AddWithValue("$sequence", sequence);
            await command.ExecuteNonQueryAsync();
        }

        private async Task ExecuteAsync(string sql)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadWrite,
                    ForeignKeys = true,
                    Pooling = false,
                    DefaultTimeout = 5
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

    private sealed record OutboxRow(
        string Status,
        int RetryCount,
        string? NextRetryAt,
        string? LastError,
        string? LeaseOwner,
        string? ClaimedAt,
        string? LeaseExpiresAt);
}
