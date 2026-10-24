using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudOutboxClaimStoreTests
{
    private static readonly DateTimeOffset ClaimedAt =
        new(2026, 10, 24, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Claims_earliest_unconfirmed_event_and_persists_lease()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync("OUTBOX-01", "BRANCH-01", 1);
        await database.SeedAsync("OUTBOX-02", "BRANCH-01", 2);
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var lease = await store.ClaimNextAsync(
            " BRANCH-01 ",
            " WORKER-01 ",
            ClaimedAt,
            TimeSpan.FromSeconds(30));

        Assert.NotNull(lease);
        Assert.Equal("OUTBOX-01", lease.OutboxId);
        Assert.Equal("BRANCH-01", lease.BranchId);
        Assert.Equal(1, lease.BranchSequence);
        Assert.Equal("REQUEST-OUTBOX-01", lease.RequestId);
        Assert.Equal("SESSION-OUTBOX-01", lease.TableSessionId.Value);
        Assert.Equal(TableSessionAction.SubmitOrder, lease.Action);
        Assert.Equal(3, lease.TableVersion);
        Assert.Equal(2, lease.RetryCount);
        Assert.Equal("WORKER-01", lease.LeaseOwner);
        Assert.Equal(ClaimedAt, lease.ClaimedAt);
        Assert.Equal(ClaimedAt.AddSeconds(30), lease.LeaseExpiresAt);

        var row = await database.ReadAsync("OUTBOX-01");
        Assert.Equal("Processing", row.Status);
        Assert.Equal("WORKER-01", row.LeaseOwner);
        Assert.Equal("2026-10-24T11:00:00.0000000+00:00", row.ClaimedAt);
        Assert.Equal("2026-10-24T11:00:30.0000000+00:00", row.LeaseExpiresAt);
    }

    [Fact]
    public async Task Future_retry_on_earliest_event_blocks_later_sequence()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            1,
            nextRetryAt: ClaimedAt.AddMinutes(1));
        await database.SeedAsync("OUTBOX-02", "BRANCH-01", 2);
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var lease = await store.ClaimNextAsync(
            "BRANCH-01",
            "WORKER-01",
            ClaimedAt,
            TimeSpan.FromSeconds(30));

        Assert.Null(lease);
        Assert.Null((await database.ReadAsync("OUTBOX-02")).LeaseOwner);
    }

    [Fact]
    public async Task Active_lease_on_earliest_event_blocks_later_sequence()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            1,
            leaseOwner: "OTHER-WORKER",
            claimedAt: ClaimedAt.AddSeconds(-10),
            leaseExpiresAt: ClaimedAt.AddSeconds(20));
        await database.SeedAsync("OUTBOX-02", "BRANCH-01", 2);
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var lease = await store.ClaimNextAsync(
            "BRANCH-01",
            "WORKER-01",
            ClaimedAt,
            TimeSpan.FromSeconds(30));

        Assert.Null(lease);
        Assert.Null((await database.ReadAsync("OUTBOX-02")).LeaseOwner);
    }

    [Fact]
    public async Task Expired_lease_on_earliest_event_can_be_reclaimed()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            1,
            leaseOwner: "OLD-WORKER",
            claimedAt: ClaimedAt.AddMinutes(-1),
            leaseExpiresAt: ClaimedAt);
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var lease = await store.ClaimNextAsync(
            "BRANCH-01",
            "NEW-WORKER",
            ClaimedAt,
            TimeSpan.FromSeconds(30));

        Assert.NotNull(lease);
        Assert.Equal("NEW-WORKER", lease.LeaseOwner);
        Assert.Equal(ClaimedAt.AddSeconds(30), lease.LeaseExpiresAt);
    }

    [Fact]
    public async Task Confirmed_earliest_event_is_skipped()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            1,
            successfullySentAt: ClaimedAt.AddMinutes(-1));
        await database.SeedAsync("OUTBOX-02", "BRANCH-01", 2);
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var lease = await store.ClaimNextAsync(
            "BRANCH-01",
            "WORKER-01",
            ClaimedAt,
            TimeSpan.FromSeconds(30));

        Assert.NotNull(lease);
        Assert.Equal("OUTBOX-02", lease.OutboxId);
    }

    [Fact]
    public async Task Branches_are_claimed_independently()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-A",
            "BRANCH-01",
            1,
            nextRetryAt: ClaimedAt.AddMinutes(1));
        await database.SeedAsync("OUTBOX-B", "BRANCH-02", 1);
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var lease = await store.ClaimNextAsync(
            "BRANCH-02",
            "WORKER-01",
            ClaimedAt,
            TimeSpan.FromSeconds(30));

        Assert.NotNull(lease);
        Assert.Equal("OUTBOX-B", lease.OutboxId);
    }

    [Fact]
    public async Task Concurrent_claim_has_one_lease_owner()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync("OUTBOX-01", "BRANCH-01", 1);
        var first = new SqliteBranchCloudOutboxClaimStore(database.Path);
        var second = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var leases = await Task.WhenAll(
            first.ClaimNextAsync(
                "BRANCH-01",
                "WORKER-01",
                ClaimedAt,
                TimeSpan.FromSeconds(30)),
            second.ClaimNextAsync(
                "BRANCH-01",
                "WORKER-02",
                ClaimedAt,
                TimeSpan.FromSeconds(30)));

        Assert.Single(leases, lease => lease is not null);
        Assert.Single(leases, lease => lease is null);
    }

    [Fact]
    public async Task Empty_branch_returns_no_lease()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        var lease = await store.ClaimNextAsync(
            "BRANCH-01",
            "WORKER-01",
            ClaimedAt,
            TimeSpan.FromSeconds(30));

        Assert.Null(lease);
    }

    [Fact]
    public async Task Unsupported_persisted_action_rolls_back_claim()
    {
        await using var database = new ClaimDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            1,
            action: "UnsupportedAction");
        var store = new SqliteBranchCloudOutboxClaimStore(database.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ClaimNextAsync(
                "BRANCH-01",
                "WORKER-01",
                ClaimedAt,
                TimeSpan.FromSeconds(30)));

        var row = await database.ReadAsync("OUTBOX-01");
        Assert.Equal("Failed", row.Status);
        Assert.Null(row.LeaseOwner);
    }

    [Theory]
    [InlineData(null, "WORKER-01", "branchId")]
    [InlineData(" ", "WORKER-01", "branchId")]
    [InlineData("BRANCH-01", null, "leaseOwner")]
    [InlineData("BRANCH-01", "", "leaseOwner")]
    public async Task Empty_identity_is_rejected(
        string? branchId,
        string? leaseOwner,
        string expectedParameter)
    {
        var store = new SqliteBranchCloudOutboxClaimStore("branch.db");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ClaimNextAsync(
                branchId!,
                leaseOwner!,
                ClaimedAt,
                TimeSpan.FromSeconds(30)));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Fact]
    public async Task Non_utc_claim_time_is_rejected()
    {
        var store = new SqliteBranchCloudOutboxClaimStore("branch.db");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ClaimNextAsync(
                "BRANCH-01",
                "WORKER-01",
                ClaimedAt.ToOffset(TimeSpan.FromHours(7)),
                TimeSpan.FromSeconds(30)));

        Assert.Equal("claimedAt", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_lease_duration_is_rejected(int seconds)
    {
        var store = new SqliteBranchCloudOutboxClaimStore("branch.db");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.ClaimNextAsync(
                "BRANCH-01",
                "WORKER-01",
                ClaimedAt,
                TimeSpan.FromSeconds(seconds)));

        Assert.Equal("leaseDuration", exception.ParamName);
    }

    [Fact]
    public async Task Missing_database_is_not_created()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-claim-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var store = new SqliteBranchCloudOutboxClaimStore(path);

        await Assert.ThrowsAsync<SqliteException>(() => store.ClaimNextAsync(
            "BRANCH-01",
            "WORKER-01",
            ClaimedAt,
            TimeSpan.FromSeconds(30)));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string databasePath)
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteBranchCloudOutboxClaimStore(databasePath));
    }

    private sealed class ClaimDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-claim-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task SeedAsync(
            string outboxId,
            string branchId,
            long branchSequence,
            DateTimeOffset? nextRetryAt = null,
            string? leaseOwner = null,
            DateTimeOffset? claimedAt = null,
            DateTimeOffset? leaseExpiresAt = null,
            DateTimeOffset? successfullySentAt = null,
            string action = "SubmitOrder")
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cloud_sync_outbox (
                    outbox_id, branch_id, request_id, table_session_id,
                    action, table_version, created_at, processing_status,
                    retry_count, next_retry_at, last_error,
                    successfully_sent_at, branch_sequence,
                    lease_owner, claimed_at, lease_expires_at)
                VALUES (
                    $outboxId, $branchId, $requestId, $tableSessionId,
                    $action, 3, '2026-10-24T10:00:00.0000000+00:00',
                    $status, 2, $nextRetryAt, 'PreviousFailure',
                    $successfullySentAt, $branchSequence,
                    $leaseOwner, $claimedAt, $leaseExpiresAt);
                """;
            command.Parameters.AddWithValue("$outboxId", outboxId);
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$requestId", $"REQUEST-{outboxId}");
            command.Parameters.AddWithValue(
                "$tableSessionId",
                $"SESSION-{outboxId}");
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue(
                "$status",
                successfullySentAt.HasValue ? "Completed" : "Failed");
            command.Parameters.AddWithValue(
                "$nextRetryAt",
                DbTimestamp(nextRetryAt));
            command.Parameters.AddWithValue(
                "$successfullySentAt",
                DbTimestamp(successfullySentAt));
            command.Parameters.AddWithValue("$branchSequence", branchSequence);
            command.Parameters.AddWithValue(
                "$leaseOwner",
                leaseOwner is null ? DBNull.Value : leaseOwner);
            command.Parameters.AddWithValue("$claimedAt", DbTimestamp(claimedAt));
            command.Parameters.AddWithValue(
                "$leaseExpiresAt",
                DbTimestamp(leaseExpiresAt));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<OutboxRow> ReadAsync(string outboxId)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT processing_status, lease_owner, claimed_at,
                       lease_expires_at
                FROM cloud_sync_outbox
                WHERE outbox_id = $outboxId;
                """;
            command.Parameters.AddWithValue("$outboxId", outboxId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new OutboxRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
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

        private static object DbTimestamp(DateTimeOffset? value) =>
            value.HasValue
                ? value.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                : DBNull.Value;

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
        string? LeaseOwner,
        string? ClaimedAt,
        string? LeaseExpiresAt);
}
