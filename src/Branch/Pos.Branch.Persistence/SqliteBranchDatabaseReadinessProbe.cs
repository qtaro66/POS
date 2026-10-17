using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchDatabaseReadinessResult(
    bool IsReady,
    int? ActualSchemaVersion,
    IReadOnlyList<string> MissingTables,
    string? ErrorCode);

public interface IBranchDatabaseReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    BranchDatabaseReadinessResult Check(string databasePath);
}

public sealed class SqliteBranchDatabaseReadinessProbe : IBranchDatabaseReadinessProbe
{
    private static readonly string[] RequiredPrintTables =
    [
        "print_jobs",
        "printer_profiles",
        "print_attempts",
        "rendered_print_payloads",
        "bill_preview_snapshots",
        "production_order_snapshots"
    ];

    public int ExpectedSchemaVersion => SqliteBranchSchema.CurrentVersion;

    public BranchDatabaseReadinessResult Check(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("The database path cannot be empty.", nameof(databasePath));
        }

        try
        {
            var fullPath = Path.GetFullPath(databasePath);
            var walPath = $"{fullPath}-wal";
            if (File.Exists(walPath) && new FileInfo(walPath).Length > 0)
            {
                return new BranchDatabaseReadinessResult(
                    false,
                    null,
                    [],
                    "UncheckpointedWalPresent");
            }

            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = $"{new Uri(fullPath).AbsoluteUri}?immutable=1",
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Private,
                    ForeignKeys = true,
                    Pooling = false,
                    DefaultTimeout = 5
                }.ToString());
            connection.Open();

            var actualVersion = ReadSchemaVersion(connection);
            var missingTables = FindMissingTables(connection);
            var errorCode = actualVersion != ExpectedSchemaVersion
                ? "SchemaVersionMismatch"
                : missingTables.Count > 0
                    ? "RequiredPrintTablesMissing"
                    : null;
            return new BranchDatabaseReadinessResult(
                errorCode is null,
                actualVersion,
                missingTables,
                errorCode);
        }
        catch (Exception exception)
            when (exception is SqliteException or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or UriFormatException)
        {
            return new BranchDatabaseReadinessResult(
                false,
                null,
                RequiredPrintTables.ToArray(),
                "DatabaseUnreadable");
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> FindMissingTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table'
              AND name IN (
                  'print_jobs',
                  'printer_profiles',
                  'print_attempts',
                  'rendered_print_payloads',
                  'bill_preview_snapshots',
                  'production_order_snapshots');
            """;
        using var reader = command.ExecuteReader();
        var found = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            found.Add(reader.GetString(0));
        }

        return RequiredPrintTables
            .Where(table => !found.Contains(table))
            .ToArray();
    }
}
