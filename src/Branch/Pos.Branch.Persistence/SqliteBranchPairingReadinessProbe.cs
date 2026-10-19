using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchPairingReadinessResult(
    bool IsReady,
    int? ActualSchemaVersion,
    IReadOnlyList<string> MissingTables,
    string? ErrorCode);

public interface IBranchPairingReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    BranchPairingReadinessResult Check(string databasePath);
}

public sealed class SqliteBranchPairingReadinessProbe : IBranchPairingReadinessProbe
{
    private static readonly string[] RequiredTables = ["branch_pairing_tokens"];

    public int ExpectedSchemaVersion => SqliteBranchSchema.CurrentVersion;

    public BranchPairingReadinessResult Check(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("The database path cannot be empty.", nameof(databasePath));
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
                    ? "PairingTablesMissing"
                    : null;
            return new BranchPairingReadinessResult(
                errorCode is null,
                actualVersion,
                missingTables,
                errorCode);
        }
        catch (Exception exception)
            when (exception is SqliteException or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return new BranchPairingReadinessResult(
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
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> FindMissingTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table' AND name = 'branch_pairing_tokens';
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
