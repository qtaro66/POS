using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudDataSentStatusQuery :
    IBranchCloudDataSentStatusQuery
{
    private static readonly string[] TimestampFormats =
    [
        "O",
        "yyyy-MM-dd'T'HH:mm:ss'Z'"
    ];

    private readonly string _connectionString;

    public SqliteBranchCloudDataSentStatusQuery(string databasePath)
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

    public async Task<BranchCloudDataSentStatus?> FindLatestAsync(
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
            SELECT successfully_sent_at
            FROM cloud_sync_outbox
            WHERE branch_id = $branchId
              AND successfully_sent_at IS NOT NULL;
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
                    "Cloud data successful-send timestamp is invalid.");
            }

            if (latest is null || parsed > latest.Value)
            {
                latest = parsed;
            }
        }

        return latest is null
            ? null
            : new BranchCloudDataSentStatus(branchId, latest.Value);
    }
}
