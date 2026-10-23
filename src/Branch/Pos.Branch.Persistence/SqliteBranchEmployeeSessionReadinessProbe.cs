using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchEmployeeSessionReadinessResult(
    bool IsReady,
    int? ActualSchemaVersion,
    IReadOnlyList<string> MissingTables,
    string? ErrorCode);

public interface IBranchEmployeeSessionReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    BranchEmployeeSessionReadinessResult Check(string databasePath);
}

public sealed class SqliteBranchEmployeeSessionReadinessProbe :
    IBranchEmployeeSessionReadinessProbe
{
    private static readonly string[] RequiredTables =
    [
        "branch_devices",
        "branch_employee_status",
        "branch_employee_sessions"
    ];

    public int ExpectedSchemaVersion => SqliteBranchSchema.CurrentVersion;

    public BranchEmployeeSessionReadinessResult Check(string databasePath)
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
            var missingTables = FindMissingTables(connection);
            var errorCode = actualVersion != ExpectedSchemaVersion
                ? "SchemaVersionMismatch"
                : missingTables.Count > 0
                    ? "EmployeeSessionTablesMissing"
                    : null;
            return new BranchEmployeeSessionReadinessResult(
                errorCode is null,
                actualVersion,
                missingTables,
                errorCode);
        }
        catch (Exception exception)
            when (exception is SqliteException or IOException or
                UnauthorizedAccessException or ArgumentException or
                NotSupportedException)
        {
            return new BranchEmployeeSessionReadinessResult(
                false,
                null,
                RequiredTables.ToArray(),
                "DatabaseUnreadable");
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> FindMissingTables(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table'
              AND name IN (
                  'branch_devices',
                  'branch_employee_status',
                  'branch_employee_sessions');
            """;
        using var reader = command.ExecuteReader();
        var found = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            found.Add(reader.GetString(0));
        }

        return RequiredTables
            .Where(table => !found.Contains(table))
            .ToArray();
    }
}
