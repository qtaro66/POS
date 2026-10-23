using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchEmployeeDataSyncStatusQuery :
    IBranchEmployeeDataSyncStatusQuery
{
    private static readonly string[] TimestampFormats =
    [
        "O",
        "yyyy-MM-dd'T'HH:mm:ss'Z'"
    ];

    private readonly string _connectionString;

    public SqliteBranchEmployeeDataSyncStatusQuery(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The database path cannot be empty.",
                nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<BranchEmployeeDataSyncStatus?> FindLatestAsync(
        string branchId,
        CancellationToken cancellationToken = default)
    {
        branchId = string.IsNullOrWhiteSpace(branchId)
            ? throw new ArgumentException(
                "The Branch identifier cannot be empty.",
                nameof(branchId))
            : branchId.Trim();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT synchronized_at
            FROM branch_employee_status
            WHERE branch_id = $branchId;
            """;
        command.Parameters.AddWithValue("$branchId", branchId);

        DateTimeOffset? latest = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var storedTimestamp = reader.GetString(0);
            if (!DateTimeOffset.TryParseExact(
                    storedTimestamp,
                    TimestampFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                throw new InvalidDataException(
                    "Employee synchronization timestamp is invalid.");
            }

            if (latest is null || parsed > latest.Value)
            {
                latest = parsed;
            }
        }

        return latest is null
            ? null
            : new BranchEmployeeDataSyncStatus(branchId, latest.Value);
    }
}
