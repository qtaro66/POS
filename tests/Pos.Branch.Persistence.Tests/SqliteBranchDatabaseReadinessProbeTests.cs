using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchDatabaseReadinessProbeTests
{
    [Fact]
    public async Task Current_initialized_schema_with_print_tables_is_ready()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var probe = new SqliteBranchDatabaseReadinessProbe();
        var bytesBefore = File.ReadAllBytes(database.Path);
        var filesBefore = database.FileNames;

        var result = probe.Check(database.Path);

        Assert.True(result.IsReady);
        Assert.Equal(probe.ExpectedSchemaVersion, result.ActualSchemaVersion);
        Assert.Empty(result.MissingTables);
        Assert.Null(result.ErrorCode);
        Assert.Equal(bytesBefore, File.ReadAllBytes(database.Path));
        Assert.Equal(filesBefore, database.FileNames);
    }

    [Fact]
    public void Empty_database_fails_without_mutating_schema()
    {
        using var database = new TemporaryDatabase();
        File.WriteAllBytes(database.Path, []);
        var probe = new SqliteBranchDatabaseReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(0, result.ActualSchemaVersion);
        Assert.Equal("SchemaVersionMismatch", result.ErrorCode);
        Assert.Equal(6, result.MissingTables.Count);
        Assert.Equal(0, new FileInfo(database.Path).Length);
    }

    [Fact]
    public async Task Current_version_missing_required_table_fails_closed()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        await database.ExecuteAsync("DROP TABLE rendered_print_payloads;");
        var probe = new SqliteBranchDatabaseReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal(probe.ExpectedSchemaVersion, result.ActualSchemaVersion);
        Assert.Equal("RequiredPrintTablesMissing", result.ErrorCode);
        Assert.Equal(["rendered_print_payloads"], result.MissingTables);
    }

    [Fact]
    public void Missing_database_is_not_created()
    {
        using var database = new TemporaryDatabase();
        var probe = new SqliteBranchDatabaseReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal("DatabaseUnreadable", result.ErrorCode);
        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public void Corrupt_database_fails_without_creating_sidecars()
    {
        using var database = new TemporaryDatabase();
        File.WriteAllBytes(database.Path, [0x01, 0x02, 0x03, 0x04]);
        var probe = new SqliteBranchDatabaseReadinessProbe();
        var bytesBefore = File.ReadAllBytes(database.Path);

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal("DatabaseUnreadable", result.ErrorCode);
        Assert.Equal(bytesBefore, File.ReadAllBytes(database.Path));
        Assert.Equal(["branch.db"], database.FileNames);
    }

    [Fact]
    public async Task Nonempty_wal_fails_closed_before_immutable_snapshot_is_read()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        File.WriteAllBytes($"{database.Path}-wal", [0x01]);
        var probe = new SqliteBranchDatabaseReadinessProbe();

        var result = probe.Check(database.Path);

        Assert.False(result.IsReady);
        Assert.Equal("UncheckpointedWalPresent", result.ErrorCode);
    }

    private sealed class TemporaryDatabase : IDisposable, IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"pos-readiness-{Guid.NewGuid():N}");

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
        }

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public string[] FileNames => Directory
            .GetFiles(_directory)
            .Select(path => System.IO.Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        public async Task InitializeAsync()
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
