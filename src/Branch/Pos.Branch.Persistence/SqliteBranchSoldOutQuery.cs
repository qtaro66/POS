using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchSoldOutRow(string SellableItemId, string? DisplayName, string? StationId, string ChangedByEmployeeId, DateTimeOffset ChangedAt);

public sealed class SqliteBranchSoldOutQuery(string databasePath)
{
    public const int PageSize = 25;

    public async Task<(long TotalCount, IReadOnlyList<BranchSoldOutRow> Items)> ReadAsync(
        string branchId, string search, int offset, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        const string source = """
            FROM branch_sold_out_overrides AS sold
            LEFT JOIN master_data_packages AS package
                ON package.branch_id = sold.branch_id AND package.lifecycle_status = 'Active'
            LEFT JOIN master_data_sellable_items AS item
                ON item.branch_id = sold.branch_id AND item.master_data_version = package.master_data_version
                AND item.sellable_item_id = sold.sellable_item_id
            WHERE sold.branch_id = $branch AND sold.is_sold_out = 1
                AND ($search = '' OR instr(lower(sold.sellable_item_id), lower($search)) > 0
                    OR instr(lower(item.display_name), lower($search)) > 0)
            """;
        command.Parameters.AddWithValue("$branch", branchId);
        command.Parameters.AddWithValue("$search", search);
        command.CommandText = "SELECT count(*) " + source;
        var total = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        command.CommandText = """
            SELECT sold.sellable_item_id, item.display_name, item.production_station_id,
                sold.changed_by_employee_id, sold.changed_at
            """ + " " + source + " ORDER BY julianday(sold.changed_at) DESC, sold.changed_at DESC, sold.sellable_item_id COLLATE BINARY LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", PageSize);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<BranchSoldOutRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime()));
        }
        return (total, items);
    }
}
