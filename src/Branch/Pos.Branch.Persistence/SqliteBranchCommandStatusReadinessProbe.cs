using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchCommandStatusReadinessResult(
    bool IsReady,
    int? ActualSchemaVersion,
    bool CommandReceiptsPresent,
    string? ErrorCode);

public interface IBranchCommandStatusReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    BranchCommandStatusReadinessResult Check(string databasePath);
}

public sealed class SqliteBranchCommandStatusReadinessProbe :
    IBranchCommandStatusReadinessProbe
{
    public int ExpectedSchemaVersion => SqliteBranchSchema.CurrentVersion;

    public BranchCommandStatusReadinessResult Check(string databasePath)
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
            var commandReceiptsPresent = HasCommandReceiptsTable(connection);
            var errorCode = actualVersion != ExpectedSchemaVersion
                ? "SchemaVersionMismatch"
                : !commandReceiptsPresent
                    ? "CommandReceiptsMissing"
                    : null;

            return new BranchCommandStatusReadinessResult(
                errorCode is null,
                actualVersion,
                commandReceiptsPresent,
                errorCode);
        }
        catch (Exception exception)
            when (exception is SqliteException or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return new BranchCommandStatusReadinessResult(
                false,
                null,
                false,
                "DatabaseUnreadable");
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool HasCommandReceiptsTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE type = 'table'
                  AND name = 'command_receipts');
            """;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }
}
