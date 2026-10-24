using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchEmployeeStatusReaderTests
{
    [Fact]
    public async Task Current_schema_creates_employee_status_cache()
    {
        await using var database = new EmployeeStatusDatabase();
        await database.InitializeAsync();

        Assert.Equal(17L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'branch_employee_status';
            """));
    }

    [Fact]
    public async Task Version_thirteen_database_adds_employee_status_cache()
    {
        await using var database = new EmployeeStatusDatabase();
        await database.InitializeAsync();
        await database.ExecuteAsync("""
            DROP TABLE branch_employee_status;
            DROP INDEX ix_cloud_sync_outbox_branch_sent;
            ALTER TABLE cloud_sync_outbox DROP COLUMN successfully_sent_at;
            PRAGMA user_version = 13;
            """);

        await database.InitializeAsync();

        Assert.Equal(17L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'branch_employee_status';
            """));
    }

    [Fact]
    public async Task Only_exact_active_employee_in_the_requested_branch_is_active()
    {
        await using var database = new EmployeeStatusDatabase();
        await database.InitializeAsync();
        await database.SeedAsync("BRANCH-01", "EMPLOYEE-01", "Active");
        await database.SeedAsync("BRANCH-01", "EMPLOYEE-02", "Revoked");
        await database.SeedAsync("BRANCH-02", "EMPLOYEE-01", "Active");
        var reader = new SqliteBranchEmployeeStatusReader(database.Path);

        Assert.True(await reader.IsActiveAsync("BRANCH-01", "EMPLOYEE-01"));
        Assert.False(await reader.IsActiveAsync("BRANCH-01", "EMPLOYEE-02"));
        Assert.False(await reader.IsActiveAsync("BRANCH-01", "EMPLOYEE-03"));
        Assert.False(await reader.IsActiveAsync("BRANCH-02", "EMPLOYEE-02"));
    }

    [Fact]
    public async Task Reader_is_read_only_and_does_not_create_missing_database()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-employee-status-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var reader = new SqliteBranchEmployeeStatusReader(path);

        await Assert.ThrowsAsync<SqliteException>(() =>
            reader.IsActiveAsync("BRANCH-01", "EMPLOYEE-01"));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("", "EMPLOYEE-01")]
    [InlineData(" ", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", "")]
    [InlineData("BRANCH-01", " ")]
    public async Task Empty_identifiers_are_rejected(
        string branchId,
        string employeeId)
    {
        var reader = new SqliteBranchEmployeeStatusReader("branch.db");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            reader.IsActiveAsync(branchId, employeeId));
    }

    private sealed class EmployeeStatusDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-employee-status-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task SeedAsync(
            string branchId,
            string employeeId,
            string lifecycleStatus)
        {
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO branch_employee_status (
                    branch_id, employee_id, lifecycle_status,
                    source_updated_at, synchronized_at)
                VALUES (
                    $branchId, $employeeId, $lifecycleStatus,
                    '2026-10-23T04:00:00Z', '2026-10-23T04:01:00Z');
                """;
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$employeeId", employeeId);
            command.Parameters.AddWithValue("$lifecycleStatus", lifecycleStatus);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Scalar query returned null.");
            return (T)Convert.ChangeType(value, typeof(T));
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

        private SqliteConnection Connection(SqliteOpenMode mode) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = mode,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
    }
}
