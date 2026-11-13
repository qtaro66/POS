using Pos.Contracts.Ordering;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteTableRegistryStore(string databasePath)
{
    private readonly SqliteConnectionFactory _connections = new(databasePath);

    public async Task ApplyAsync(TableRegistry registry, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO master_data_table_registries (branch_id, master_data_version)
            SELECT branch_id, master_data_version FROM master_data_packages
            WHERE branch_id = $branch AND master_data_version = $version AND lifecycle_status = 'Active'
            ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("$branch", registry.BranchId);
        command.Parameters.AddWithValue("$version", registry.MasterDataVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) { return; }

        command.CommandText = """
            INSERT INTO master_data_tables (branch_id, master_data_version, table_id, display_name, zone, display_order)
            VALUES ($branch, $version, $id, $name, $zone, $order);
            """;
        var id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
        var name = command.Parameters.Add("$name", Microsoft.Data.Sqlite.SqliteType.Text);
        var zone = command.Parameters.Add("$zone", Microsoft.Data.Sqlite.SqliteType.Text);
        var order = command.Parameters.Add("$order", Microsoft.Data.Sqlite.SqliteType.Integer);
        foreach (var table in registry.Tables)
        {
            id.Value = table.TableId;
            name.Value = table.DisplayName;
            zone.Value = (object?)table.Zone ?? DBNull.Value;
            order.Value = table.DisplayOrder;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
