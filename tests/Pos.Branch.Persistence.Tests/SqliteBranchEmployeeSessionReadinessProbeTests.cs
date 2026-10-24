using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchEmployeeSessionReadinessProbeTests
{
    [Fact]
    public async Task Current_schema_is_ready()
    {
        await using var database = new ProbeDatabase();
        await database.InitializeAsync();
        var probe = new SqliteBranchEmployeeSessionReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.True(result.IsReady);
        Assert.Equal(16, result.ActualSchemaVersion);
        Assert.Empty(result.MissingTables);
        Assert.Null(result.ErrorCode);
        Assert.Equal(16, probe.ExpectedSchemaVersion);
    }

    [Fact]
    public async Task Wrong_schema_version_fails_before_runtime_composition()
    {
        await using var database = new ProbeDatabase();
        await database.InitializeAsync();
        await database.ExecuteAsync("PRAGMA user_version = 12;");
        var probe = new SqliteBranchEmployeeSessionReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(12, result.ActualSchemaVersion);
        Assert.Empty(result.MissingTables);
        Assert.Equal("SchemaVersionMismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_session_tables_are_reported_exactly()
    {
        await using var database = new ProbeDatabase();
        await database.CreateEmptyCurrentVersionAsync();
        var probe = new SqliteBranchEmployeeSessionReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(16, result.ActualSchemaVersion);
        Assert.Equal(
            ["branch_devices", "branch_employee_status", "branch_employee_sessions"],
            result.MissingTables);
        Assert.Equal("EmployeeSessionTablesMissing", result.ErrorCode);
    }

    [Fact]
    public void Missing_database_is_unreadable_and_is_not_created()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-session-readiness-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var probe = new SqliteBranchEmployeeSessionReadinessProbe();

        var result = probe.Check(path);

        Assert.False(result.IsReady);
        Assert.Null(result.ActualSchemaVersion);
        Assert.Equal(
            ["branch_devices", "branch_employee_status", "branch_employee_sessions"],
            result.MissingTables);
        Assert.Equal("DatabaseUnreadable", result.ErrorCode);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string path)
    {
        var probe = new SqliteBranchEmployeeSessionReadinessProbe();

        Assert.Throws<ArgumentException>(() => probe.Check(path));
    }

    private sealed class ProbeDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-session-readiness-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task CreateEmptyCurrentVersionAsync()
        {
            Directory.CreateDirectory(_directory);
            await ExecuteAsync("PRAGMA user_version = 16;", create: true);
        }

        public Task ExecuteAsync(string sql) => ExecuteAsync(sql, create: false);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private async Task ExecuteAsync(string sql, bool create)
        {
            Directory.CreateDirectory(_directory);
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = create
                        ? SqliteOpenMode.ReadWriteCreate
                        : SqliteOpenMode.ReadWrite,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
