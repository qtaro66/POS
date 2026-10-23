using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchCloudDataSentStatusReadinessResult(
    bool IsReady,
    int? ActualSchemaVersion,
    bool CloudSyncOutboxPresent,
    IReadOnlyList<string> MissingColumns,
    string? ErrorCode);

public interface IBranchCloudDataSentStatusReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    BranchCloudDataSentStatusReadinessResult Check(string databasePath);
}

public sealed class SqliteBranchCloudDataSentStatusReadinessProbe :
    IBranchCloudDataSentStatusReadinessProbe
{
    private static readonly string[] RequiredColumns =
    [
        "branch_id",
        "successfully_sent_at"
    ];

    public int ExpectedSchemaVersion => SqliteBranchSchema.CurrentVersion;

    public BranchCloudDataSentStatusReadinessResult Check(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The database path cannot be empty.",
                nameof(databasePath));
        }

        try
        {
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.GetFullPath(databasePath),
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Private,
                    ForeignKeys = true,
                    Pooling = false,
                    DefaultTimeout = 5
                }.ToString());
            connection.Open();

            var actualVersion = ReadSchemaVersion(connection);
            var tablePresent = HasCloudSyncOutboxTable(connection);
            var missingColumns = tablePresent
                ? FindMissingColumns(connection)
                : RequiredColumns.ToArray();
            var errorCode = actualVersion != ExpectedSchemaVersion
                ? "SchemaVersionMismatch"
                : !tablePresent
                    ? "CloudSyncOutboxMissing"
                    : missingColumns.Count > 0
                        ? "CloudDataSentColumnsMissing"
                        : null;

            return new BranchCloudDataSentStatusReadinessResult(
                errorCode is null,
                actualVersion,
                tablePresent,
                missingColumns,
                errorCode);
        }
        catch (Exception exception)
            when (exception is SqliteException or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return new BranchCloudDataSentStatusReadinessResult(
                false,
                null,
                false,
                RequiredColumns.ToArray(),
                "DatabaseUnreadable");
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool HasCloudSyncOutboxTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE type = 'table'
                  AND name = 'cloud_sync_outbox');
            """;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static IReadOnlyList<string> FindMissingColumns(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(cloud_sync_outbox);";
        using var reader = command.ExecuteReader();
        var found = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            found.Add(reader.GetString(1));
        }

        return RequiredColumns
            .Where(column => !found.Contains(column))
            .ToArray();
    }
}
