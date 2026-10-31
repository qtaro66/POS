using Microsoft.Data.Sqlite;
using Pos.Application.MasterData;
using Pos.Application.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteBranchOrderingQuery : IBranchOrderingQuery
{
    private readonly string _connectionString;
    public SqliteBranchOrderingQuery(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<IReadOnlyList<BranchActiveTableSession>> ListActiveAsync(string branchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_session_id, table_id, status, version FROM table_sessions
            WHERE branch_id = $branchId AND status IN ('Ordering','PaymentPending','PaymentInProgress','PartiallyPaid')
            ORDER BY table_id, table_session_id;
            """;
        command.Parameters.AddWithValue("$branchId", branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<BranchActiveTableSession>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        }
        return result;
    }

    public async Task<BranchSessionMenu?> MenuAsync(string branchId, string tableSessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableSessionId);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        string? version;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT p.master_data_version FROM table_session_billing_policy p
                JOIN table_sessions s ON s.branch_id = p.branch_id AND s.table_session_id = p.table_session_id
                WHERE p.branch_id = $branchId AND p.table_session_id = $sessionId
                  AND s.status IN ('Ordering','PaymentPending','PaymentInProgress','PartiallyPaid');
                """;
            read.Parameters.AddWithValue("$branchId", branchId);
            read.Parameters.AddWithValue("$sessionId", tableSessionId);
            version = await read.ExecuteScalarAsync(cancellationToken) as string;
        }
        if (version is null) { return null; }
        var ids = new List<string>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT sellable_item_id FROM master_data_sellable_items
                WHERE branch_id = $branchId AND master_data_version = $version AND item_kind = 'Item'
                ORDER BY sellable_item_id;
                """;
            read.Parameters.AddWithValue("$branchId", branchId);
            read.Parameters.AddWithValue("$version", version);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) { ids.Add(reader.GetString(0)); }
        }
        var items = new List<CatalogResolutionContext>();
        foreach (var id in ids)
        {
            var item = await SqliteCatalogSnapshotResolver.ReadItemAsync(connection, transaction, branchId, version, id, cancellationToken)
                ?? throw new InvalidOperationException("The frozen menu item disappeared within its read transaction.");
            var groups = await SqliteCatalogSnapshotResolver.ReadGroupsAsync(connection, transaction, branchId, version, id, cancellationToken);
            var relevantIds = groups.SelectMany(group => group.Options.Keys).Append(id).Distinct(StringComparer.Ordinal).ToArray();
            var availability = await SqliteCatalogSnapshotResolver.ReadCurrentAvailabilityAsync(connection, transaction, branchId, relevantIds, cancellationToken);
            items.Add(new(version, item, groups, availability));
        }
        await transaction.CommitAsync(cancellationToken);
        return new(tableSessionId, version, items);
    }
}
