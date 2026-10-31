using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public static class SqliteBranchOrderingReadinessProbe
{
    public static bool IsReady(string databasePath)
    {
        if (!new SqliteBranchCommandStatusReadinessProbe().Check(databasePath).IsReady) { return false; }
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(databasePath),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT table_session_id, table_id, status, version FROM table_sessions LIMIT 0;
                SELECT master_data_version FROM table_session_billing_policy LIMIT 0;
                SELECT lifecycle_status, master_data_version FROM master_data_packages LIMIT 0;
                SELECT calculation_policy_version FROM master_data_billing_policy LIMIT 0;
                SELECT sellable_item_id, resolved_price, production_station_id FROM master_data_sellable_items LIMIT 0;
                SELECT option_group_id FROM master_data_option_groups LIMIT 0;
                SELECT option_sellable_item_id FROM master_data_option_group_items LIMIT 0;
                SELECT is_sold_out FROM branch_sold_out_overrides LIMIT 0;
                SELECT order_batch_id FROM order_batches LIMIT 0;
                SELECT order_line_id FROM order_lines LIMIT 0;
                SELECT order_line_id FROM order_line_options LIMIT 0;
                SELECT audit_id FROM table_session_audit LIMIT 0;
                SELECT outbox_id FROM cloud_sync_outbox LIMIT 0;
                SELECT snapshot_id FROM production_order_snapshots LIMIT 0;
                SELECT print_job_id FROM print_jobs LIMIT 0;
                """;
            using var reader = command.ExecuteReader();
            while (reader.NextResult()) { }
            return true;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
