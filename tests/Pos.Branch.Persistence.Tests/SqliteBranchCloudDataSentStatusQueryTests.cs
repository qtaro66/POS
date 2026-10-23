using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudDataSentStatusQueryTests
{
    [Fact]
    public async Task Latest_successful_timestamp_is_returned_for_requested_branch()
    {
        await using var database = new DataSentStatusDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            "2026-10-23T04:00:00Z");
        await database.SeedAsync(
            "OUTBOX-02",
            "BRANCH-01",
            "2026-10-23T05:30:00.0000000+00:00");
        await database.SeedAsync(
            "OUTBOX-03",
            "BRANCH-01",
            null);
        await database.SeedAsync(
            "OUTBOX-04",
            "BRANCH-02",
            "2026-10-23T06:00:00Z");
        var query = new SqliteBranchCloudDataSentStatusQuery(database.Path);

        var result = await query.FindLatestAsync("BRANCH-01");

        Assert.NotNull(result);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 23, 5, 30, 0, TimeSpan.Zero),
            result.LastSuccessfullySentAt);
    }

    [Fact]
    public async Task Branch_without_successful_send_returns_no_status()
    {
        await using var database = new DataSentStatusDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            null);
        var query = new SqliteBranchCloudDataSentStatusQuery(database.Path);

        var result = await query.FindLatestAsync("BRANCH-01");

        Assert.Null(result);
    }

    [Fact]
    public async Task Invalid_successful_send_timestamp_fails_closed()
    {
        await using var database = new DataSentStatusDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "OUTBOX-01",
            "BRANCH-01",
            "not-a-timestamp");
        var query = new SqliteBranchCloudDataSentStatusQuery(database.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            query.FindLatestAsync("BRANCH-01"));
    }

    [Fact]
    public async Task Query_is_read_only_and_does_not_create_missing_database()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-data-sent-status-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var query = new SqliteBranchCloudDataSentStatusQuery(path);

        await Assert.ThrowsAsync<SqliteException>(() =>
            query.FindLatestAsync("BRANCH-01"));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Empty_branch_identifier_is_rejected(string branchId)
    {
        var query = new SqliteBranchCloudDataSentStatusQuery("branch.db");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            query.FindLatestAsync(branchId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string databasePath)
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteBranchCloudDataSentStatusQuery(databasePath));
    }

    private sealed class DataSentStatusDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-data-sent-status-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task SeedAsync(
            string outboxId,
            string branchId,
            string? successfullySentAt)
        {
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadWrite,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cloud_sync_outbox (
                    outbox_id, branch_id, request_id, table_session_id,
                    action, table_version, created_at, processing_status,
                    retry_count, successfully_sent_at)
                VALUES (
                    $outboxId, $branchId, $requestId, $tableSessionId,
                    'SubmitOrder', 1, '2026-10-23T03:00:00Z', 'Completed',
                    0, $successfullySentAt);
                """;
            command.Parameters.AddWithValue("$outboxId", outboxId);
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$requestId", $"REQUEST-{outboxId}");
            command.Parameters.AddWithValue("$tableSessionId", $"SESSION-{outboxId}");
            command.Parameters.AddWithValue(
                "$successfullySentAt",
                successfullySentAt is null ? DBNull.Value : successfullySentAt);
            await command.ExecuteNonQueryAsync();
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
