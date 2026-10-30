using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchCloudOutboxReadinessResult(
    bool IsReady,
    int? ActualSchemaVersion,
    bool CloudSyncOutboxPresent,
    bool DeliveryFailuresPresent,
    IReadOnlyList<string> MissingOutboxColumns,
    IReadOnlyList<string> MissingDeliveryFailureColumns,
    string? ErrorCode);

public interface IBranchCloudOutboxReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    BranchCloudOutboxReadinessResult Check(string databasePath);
}

public sealed class SqliteBranchCloudOutboxReadinessProbe :
    IBranchCloudOutboxReadinessProbe
{
    private const string OutboxTable = "cloud_sync_outbox";
    private const string DeliveryFailuresTable = "cloud_sync_delivery_failures";

    private static readonly string[] RequiredOutboxColumns =
    [
        "outbox_id",
        "branch_id",
        "branch_sequence",
        "request_id",
        "table_session_id",
        "action",
        "table_version",
        "created_at",
        "processing_status",
        "retry_count",
        "next_retry_at",
        "last_error",
        "successfully_sent_at",
        "lease_owner",
        "claimed_at",
        "lease_expires_at"
    ];

    private static readonly string[] RequiredDeliveryFailureColumns =
    [
        "failure_id",
        "branch_id",
        "outbox_id",
        "branch_sequence",
        "lease_owner",
        "claimed_at",
        "lease_expires_at",
        "failed_at",
        "next_retry_at",
        "error_code",
        "retry_count_after",
        "expected_branch_sequence"
    ];

    public int ExpectedSchemaVersion => SqliteBranchSchema.CurrentVersion;

    public BranchCloudOutboxReadinessResult Check(string databasePath)
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
            var outboxPresent = HasTable(connection, OutboxTable);
            var failuresPresent = HasTable(connection, DeliveryFailuresTable);
            var missingOutboxColumns = outboxPresent
                ? FindMissingColumns(connection, OutboxTable, RequiredOutboxColumns)
                : RequiredOutboxColumns.ToArray();
            var missingFailureColumns = failuresPresent
                ? FindMissingColumns(
                    connection,
                    DeliveryFailuresTable,
                    RequiredDeliveryFailureColumns)
                : RequiredDeliveryFailureColumns.ToArray();
            var errorCode = DetermineErrorCode(
                actualVersion,
                outboxPresent,
                failuresPresent,
                missingOutboxColumns,
                missingFailureColumns);

            return new BranchCloudOutboxReadinessResult(
                errorCode is null,
                actualVersion,
                outboxPresent,
                failuresPresent,
                missingOutboxColumns,
                missingFailureColumns,
                errorCode);
        }
        catch (Exception exception)
            when (exception is SqliteException or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return new BranchCloudOutboxReadinessResult(
                false,
                null,
                false,
                false,
                RequiredOutboxColumns.ToArray(),
                RequiredDeliveryFailureColumns.ToArray(),
                "DatabaseUnreadable");
        }
    }

    private string? DetermineErrorCode(
        int actualVersion,
        bool outboxPresent,
        bool failuresPresent,
        IReadOnlyCollection<string> missingOutboxColumns,
        IReadOnlyCollection<string> missingFailureColumns) =>
        actualVersion != ExpectedSchemaVersion
            ? "SchemaVersionMismatch"
            : !outboxPresent
                ? "CloudSyncOutboxMissing"
                : missingOutboxColumns.Count > 0
                    ? "CloudSyncOutboxColumnsMissing"
                    : !failuresPresent
                        ? "CloudDeliveryFailuresMissing"
                        : missingFailureColumns.Count > 0
                            ? "CloudDeliveryFailureColumnsMissing"
                            : null;

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool HasTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE type = 'table'
                  AND name = $tableName);
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static IReadOnlyList<string> FindMissingColumns(
        SqliteConnection connection,
        string tableName,
        IEnumerable<string> requiredColumns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        var found = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            found.Add(reader.GetString(1));
        }

        return requiredColumns
            .Where(column => !found.Contains(column))
            .ToArray();
    }
}
