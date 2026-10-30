using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudOutboxReadinessProbeTests
{
    [Fact]
    public async Task Current_schema_is_ready()
    {
        await using var database = new ProbeDatabase();
        await database.InitializeAsync();
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.True(result.IsReady);
        Assert.Equal(20, result.ActualSchemaVersion);
        Assert.True(result.CloudSyncOutboxPresent);
        Assert.True(result.DeliveryFailuresPresent);
        Assert.Empty(result.MissingOutboxColumns);
        Assert.Empty(result.MissingDeliveryFailureColumns);
        Assert.Null(result.ErrorCode);
        Assert.Equal(20, probe.ExpectedSchemaVersion);
    }

    [Fact]
    public async Task Wrong_schema_version_has_highest_failure_precedence()
    {
        await using var database = new ProbeDatabase();
        await database.CreateEmptyCurrentVersionAsync();
        await database.ExecuteAsync("PRAGMA user_version = 17;");
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(17, result.ActualSchemaVersion);
        Assert.Equal("SchemaVersionMismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_outbox_table_is_reported_before_failure_table()
    {
        await using var database = new ProbeDatabase();
        await database.CreateEmptyCurrentVersionAsync();
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.False(result.CloudSyncOutboxPresent);
        Assert.False(result.DeliveryFailuresPresent);
        Assert.Contains("lease_expires_at", result.MissingOutboxColumns);
        Assert.Equal("CloudSyncOutboxMissing", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_outbox_column_is_reported_before_failure_table()
    {
        await using var database = new ProbeDatabase();
        await database.CreatePartialSchemaAsync(
            includeOutboxLeaseExpiry: false,
            includeFailureRetryCount: true,
            includeFailureTable: true);
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(["lease_expires_at"], result.MissingOutboxColumns);
        Assert.Equal("CloudSyncOutboxColumnsMissing", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_delivery_failure_table_is_reported()
    {
        await using var database = new ProbeDatabase();
        await database.CreatePartialSchemaAsync(
            includeOutboxLeaseExpiry: true,
            includeFailureRetryCount: true,
            includeFailureTable: false);
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.True(result.CloudSyncOutboxPresent);
        Assert.False(result.DeliveryFailuresPresent);
        Assert.Contains("failure_id", result.MissingDeliveryFailureColumns);
        Assert.Equal("CloudDeliveryFailuresMissing", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_delivery_failure_column_is_reported()
    {
        await using var database = new ProbeDatabase();
        await database.CreatePartialSchemaAsync(
            includeOutboxLeaseExpiry: true,
            includeFailureRetryCount: false,
            includeFailureTable: true);
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(["retry_count_after"], result.MissingDeliveryFailureColumns);
        Assert.Equal("CloudDeliveryFailureColumnsMissing", result.ErrorCode);
    }

    [Fact]
    public void Missing_database_is_unreadable_and_is_not_created()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "pos-cloud-outbox-readiness-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "missing.db");
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        var result = probe.Check(path);

        Assert.False(result.IsReady);
        Assert.Null(result.ActualSchemaVersion);
        Assert.Equal("DatabaseUnreadable", result.ErrorCode);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string path)
    {
        var probe = new SqliteBranchCloudOutboxReadinessProbe();

        Assert.Throws<ArgumentException>(() => probe.Check(path));
    }

    private sealed class ProbeDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-outbox-readiness-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public Task CreateEmptyCurrentVersionAsync() =>
            ExecuteAsync("PRAGMA user_version = 20;", create: true);

        public Task CreatePartialSchemaAsync(
            bool includeOutboxLeaseExpiry,
            bool includeFailureRetryCount,
            bool includeFailureTable)
        {
            var outboxLeaseExpiry = includeOutboxLeaseExpiry
                ? ", lease_expires_at TEXT NULL"
                : string.Empty;
            var failureRetryCount = includeFailureRetryCount
                ? ", retry_count_after INTEGER NOT NULL"
                : string.Empty;
            var failureTable = includeFailureTable
                ? $$"""
                    CREATE TABLE cloud_sync_delivery_failures (
                        failure_id TEXT PRIMARY KEY,
                        branch_id TEXT NOT NULL,
                        outbox_id TEXT NOT NULL,
                        branch_sequence INTEGER NOT NULL,
                        lease_owner TEXT NOT NULL,
                        claimed_at TEXT NOT NULL,
                        lease_expires_at TEXT NOT NULL,
                        failed_at TEXT NOT NULL,
                        next_retry_at TEXT NOT NULL,
                        error_code TEXT NOT NULL,
                        expected_branch_sequence INTEGER NULL
                        {{failureRetryCount}});
                    """
                : string.Empty;

            return ExecuteAsync(
                $$"""
                PRAGMA user_version = 20;
                CREATE TABLE cloud_sync_outbox (
                    outbox_id TEXT PRIMARY KEY,
                    branch_id TEXT NOT NULL,
                    branch_sequence INTEGER NOT NULL,
                    request_id TEXT NOT NULL,
                    table_session_id TEXT NOT NULL,
                    action TEXT NOT NULL,
                    table_version INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    processing_status TEXT NOT NULL,
                    retry_count INTEGER NOT NULL,
                    next_retry_at TEXT NULL,
                    last_error TEXT NULL,
                    successfully_sent_at TEXT NULL,
                    lease_owner TEXT NULL,
                    claimed_at TEXT NULL
                    {{outboxLeaseExpiry}});
                {{failureTable}}
                """,
                create: true);
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
