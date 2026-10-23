using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchEmployeeDataSyncStatusQueryTests
{
    [Fact]
    public async Task Latest_valid_timestamp_is_returned_for_requested_branch()
    {
        await using var database = new SyncStatusDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "BRANCH-01",
            "EMPLOYEE-01",
            "2026-10-23T04:00:00Z");
        await database.SeedAsync(
            "BRANCH-01",
            "EMPLOYEE-02",
            "2026-10-23T05:30:00.0000000+00:00");
        await database.SeedAsync(
            "BRANCH-02",
            "EMPLOYEE-03",
            "2026-10-23T06:00:00Z");
        var query = new SqliteBranchEmployeeDataSyncStatusQuery(database.Path);

        var result = await query.FindLatestAsync("BRANCH-01");

        Assert.NotNull(result);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 23, 5, 30, 0, TimeSpan.Zero),
            result.LastSynchronizedAt);
    }

    [Fact]
    public async Task Branch_without_employee_status_returns_no_sync_status()
    {
        await using var database = new SyncStatusDatabase();
        await database.InitializeAsync();
        var query = new SqliteBranchEmployeeDataSyncStatusQuery(database.Path);

        var result = await query.FindLatestAsync("BRANCH-01");

        Assert.Null(result);
    }

    [Fact]
    public async Task Invalid_timestamp_fails_closed()
    {
        await using var database = new SyncStatusDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(
            "BRANCH-01",
            "EMPLOYEE-01",
            "not-a-timestamp");
        var query = new SqliteBranchEmployeeDataSyncStatusQuery(database.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            query.FindLatestAsync("BRANCH-01"));
    }

    [Fact]
    public async Task Query_is_read_only_and_does_not_create_missing_database()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-employee-sync-status-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var query = new SqliteBranchEmployeeDataSyncStatusQuery(path);

        await Assert.ThrowsAsync<SqliteException>(() =>
            query.FindLatestAsync("BRANCH-01"));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Empty_branch_identifier_is_rejected(string branchId)
    {
        var query = new SqliteBranchEmployeeDataSyncStatusQuery("branch.db");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            query.FindLatestAsync(branchId));
    }

    private sealed class SyncStatusDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-employee-sync-status-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task SeedAsync(
            string branchId,
            string employeeId,
            string synchronizedAt)
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
                INSERT INTO branch_employee_status (
                    branch_id, employee_id, lifecycle_status,
                    source_updated_at, synchronized_at)
                VALUES (
                    $branchId, $employeeId, 'Active',
                    '2026-10-23T03:00:00Z', $synchronizedAt);
                """;
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$employeeId", employeeId);
            command.Parameters.AddWithValue("$synchronizedAt", synchronizedAt);
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
