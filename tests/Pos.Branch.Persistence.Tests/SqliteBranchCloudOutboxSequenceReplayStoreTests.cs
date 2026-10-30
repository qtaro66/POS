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
