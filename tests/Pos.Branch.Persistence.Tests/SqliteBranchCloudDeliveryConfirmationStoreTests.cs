using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudDeliveryConfirmationStoreTests
{
    [Fact]
    public async Task First_confirmation_completes_matching_outbox_atomically()
    {
        await using var database = new ConfirmationDatabase();
        await database.InitializeAsync();
        await database.SeedAsync("OUTBOX-01", "BRANCH-01");
        var store = new SqliteBranchCloudDeliveryConfirmationStore(database.Path);
        var acknowledgedAt = new DateTimeOffset(
            2026,
            10,
            24,
            4,
            45,
            0,
            TimeSpan.Zero);

        var result = await store.ConfirmAsync(
            new BranchCloudDeliveryConfirmation(
                "BRANCH-01",
                "OUTBOX-01",
                acknowledgedAt));

        Assert.Equal(BranchCloudDeliveryConfirmationResult.Confirmed, result);
        var row = await database.ReadAsync("OUTBOX-01");
        Assert.Equal("Completed", row.Status);
        Assert.Equal(3, row.RetryCount);
        Assert.Null(row.NextRetryAt);
        Assert.Null(row.LastError);
        Assert.Equal("2026-10-24T04:45:00.0000000+00:00", row.SuccessfullySentAt);
    }

    [Fact]
    public async Task Repeated_confirmation_preserves_first_timestamp()
    {
        await using var database = new ConfirmationDatabase();
        await database.InitializeAsync();
        await database.SeedAsync("OUTBOX-01", "BRANCH-01");
        var store = new SqliteBranchCloudDeliveryConfirmationStore(database.Path);
        var first = new DateTimeOffset(
            2026,
            10,
            24,
            4,
            45,
            0,
            TimeSpan.Zero);
        var later = first.AddMinutes(5);
        await store.ConfirmAsync(
            new BranchCloudDeliveryConfirmation("BRANCH-01", "OUTBOX-01", first));

        var result = await store.ConfirmAsync(
            new BranchCloudDeliveryConfirmation("BRANCH-01", "OUTBOX-01", later));

        Assert.Equal(
            BranchCloudDeliveryConfirmationResult.AlreadyConfirmed,
            result);
        Assert.Equal(
            "2026-10-24T04:45:00.0000000+00:00",
            (await database.ReadAsync("OUTBOX-01")).SuccessfullySentAt);
    }

    [Fact]
    public async Task Concurrent_confirmation_has_one_first_writer()
    {
        await using var database = new ConfirmationDatabase();
        await database.InitializeAsync();
        await database.SeedAsync("OUTBOX-01", "BRANCH-01");
        var store = new SqliteBranchCloudDeliveryConfirmationStore(database.Path);
        var acknowledgedAt = new DateTimeOffset(
            2026,
            10,
            24,
            4,
            45,
            0,
            TimeSpan.Zero);
        var confirmation = new BranchCloudDeliveryConfirmation(
            "BRANCH-01",
            "OUTBOX-01",
            acknowledgedAt);

        var results = await Task.WhenAll(
            store.ConfirmAsync(confirmation),
            store.ConfirmAsync(confirmation));

        Assert.Single(
            results,
            result => result == BranchCloudDeliveryConfirmationResult.Confirmed);
        Assert.Single(
            results,
            result => result ==
                BranchCloudDeliveryConfirmationResult.AlreadyConfirmed);
        Assert.Equal(
            "2026-10-24T04:45:00.0000000+00:00",
            (await database.ReadAsync("OUTBOX-01")).SuccessfullySentAt);
    }

    [Theory]
    [InlineData("BRANCH-02", "OUTBOX-01")]
    [InlineData("BRANCH-01", "OUTBOX-MISSING")]
    public async Task Missing_or_cross_branch_outbox_returns_not_found(
        string branchId,
        string outboxId)
    {
        await using var database = new ConfirmationDatabase();
        await database.InitializeAsync();
        await database.SeedAsync("OUTBOX-01", "BRANCH-01");
        var store = new SqliteBranchCloudDeliveryConfirmationStore(database.Path);

        var result = await store.ConfirmAsync(
            new BranchCloudDeliveryConfirmation(
                branchId,
                outboxId,
                DateTimeOffset.UnixEpoch));

        Assert.Equal(BranchCloudDeliveryConfirmationResult.NotFound, result);
        var row = await database.ReadAsync("OUTBOX-01");
        Assert.Equal("Failed", row.Status);
        Assert.Null(row.SuccessfullySentAt);
    }

    [Fact]
    public async Task Missing_database_is_not_created()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-confirmation-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var store = new SqliteBranchCloudDeliveryConfirmationStore(path);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.ConfirmAsync(new BranchCloudDeliveryConfirmation(
                "BRANCH-01",
                "OUTBOX-01",
                DateTimeOffset.UnixEpoch)));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Missing_confirmation_is_rejected()
    {
        var store = new SqliteBranchCloudDeliveryConfirmationStore("branch.db");

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.ConfirmAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string databasePath)
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteBranchCloudDeliveryConfirmationStore(databasePath));
    }

    private sealed class ConfirmationDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-confirmation-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task SeedAsync(string outboxId, string branchId)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cloud_sync_outbox (
                    outbox_id, branch_id, request_id, table_session_id,
                    action, table_version, created_at, processing_status,
                    retry_count, next_retry_at, last_error,
                    successfully_sent_at)
                VALUES (
                    $outboxId, $branchId, $requestId, $tableSessionId,
                    'SubmitOrder', 1, '2026-10-24T04:00:00Z', 'Failed',
                    3, '2026-10-24T04:10:00Z', 'CloudUnavailable', NULL);
                """;
            command.Parameters.AddWithValue("$outboxId", outboxId);
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$requestId", $"REQUEST-{outboxId}");
            command.Parameters.AddWithValue("$tableSessionId", $"SESSION-{outboxId}");
            await command.ExecuteNonQueryAsync();
        }

        public async Task<OutboxRow> ReadAsync(string outboxId)
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT processing_status, retry_count, next_retry_at,
                       last_error, successfully_sent_at
                FROM cloud_sync_outbox
                WHERE outbox_id = $outboxId;
                """;
            command.Parameters.AddWithValue("$outboxId", outboxId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new OutboxRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }

        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadWrite,
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

    private sealed record OutboxRow(
        string Status,
        int RetryCount,
        string? NextRetryAt,
        string? LastError,
        string? SuccessfullySentAt);
}
