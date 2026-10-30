using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudDataSentStatusReadinessProbeTests
{
    [Fact]
    public async Task Current_schema_is_ready()
    {
        await using var database = new ProbeDatabase();
        await database.InitializeAsync();
        var probe = new SqliteBranchCloudDataSentStatusReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.True(result.IsReady);
        Assert.Equal(20, result.ActualSchemaVersion);
        Assert.True(result.CloudSyncOutboxPresent);
        Assert.Empty(result.MissingColumns);
        Assert.Null(result.ErrorCode);
        Assert.Equal(20, probe.ExpectedSchemaVersion);
    }

    [Fact]
    public async Task Wrong_schema_version_fails_before_runtime_composition()
    {
        await using var database = new ProbeDatabase();
        await database.InitializeAsync();
        await database.ExecuteAsync("PRAGMA user_version = 14;");
        var probe = new SqliteBranchCloudDataSentStatusReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(14, result.ActualSchemaVersion);
        Assert.True(result.CloudSyncOutboxPresent);
        Assert.Empty(result.MissingColumns);
        Assert.Equal("SchemaVersionMismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_outbox_table_is_reported_exactly()
    {
        await using var database = new ProbeDatabase();
        await database.CreateEmptyCurrentVersionAsync();
        var probe = new SqliteBranchCloudDataSentStatusReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(20, result.ActualSchemaVersion);
        Assert.False(result.CloudSyncOutboxPresent);
        Assert.Equal(
            ["branch_id", "successfully_sent_at"],
            result.MissingColumns);
        Assert.Equal("CloudSyncOutboxMissing", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_success_timestamp_column_is_reported_exactly()
    {
        await using var database = new ProbeDatabase();
        await database.CreateOutboxWithoutSuccessTimestampAsync();
        var probe = new SqliteBranchCloudDataSentStatusReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(20, result.ActualSchemaVersion);
        Assert.True(result.CloudSyncOutboxPresent);
        Assert.Equal(["successfully_sent_at"], result.MissingColumns);
        Assert.Equal("CloudDataSentColumnsMissing", result.ErrorCode);
    }

    [Fact]
    public void Missing_database_is_unreadable_and_is_not_created()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-data-sent-readiness-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var probe = new SqliteBranchCloudDataSentStatusReadinessProbe();

        var result = probe.Check(path);

        Assert.False(result.IsReady);
        Assert.Null(result.ActualSchemaVersion);
        Assert.False(result.CloudSyncOutboxPresent);
        Assert.Equal(
            ["branch_id", "successfully_sent_at"],
            result.MissingColumns);
        Assert.Equal("DatabaseUnreadable", result.ErrorCode);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string path)
    {
        var probe = new SqliteBranchCloudDataSentStatusReadinessProbe();

        Assert.Throws<ArgumentException>(() => probe.Check(path));
    }

    private sealed class ProbeDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-data-sent-readiness-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public Task CreateEmptyCurrentVersionAsync() =>
            ExecuteAsync("PRAGMA user_version = 20;", create: true);

        public Task CreateOutboxWithoutSuccessTimestampAsync() =>
            ExecuteAsync(
                """
                PRAGMA user_version = 20;
                CREATE TABLE cloud_sync_outbox (
                    outbox_id TEXT PRIMARY KEY,
                    branch_id TEXT NOT NULL);
                """,
                create: true);

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
