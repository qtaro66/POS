using Microsoft.Data.Sqlite;
using Pos.Application.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteBranchOrderingQuery
{
    public async Task<BranchFloorPlan?> FloorPlanAsync(string branchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH registry AS (
                SELECT r.branch_id, r.master_data_version
                FROM master_data_table_registries r
                JOIN master_data_packages p USING (branch_id, master_data_version)
                WHERE r.branch_id = $branchId AND p.lifecycle_status = 'Active'
            ), active AS (
                SELECT table_id, table_session_id, status, version
                FROM table_sessions
                WHERE branch_id = $branchId
                  AND status IN ('Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid')
            ), visible AS (
                SELECT t.table_id, t.display_name, t.zone, t.display_order,
                       COALESCE(a.status, 'Available') AS status, a.table_session_id, a.version
                FROM master_data_tables t
                JOIN registry USING (branch_id, master_data_version)
                LEFT JOIN active a ON a.table_id = t.table_id
                UNION ALL
                SELECT a.table_id, a.table_id, NULL, 2147483647, a.status, a.table_session_id, a.version
                FROM active a
                WHERE NOT EXISTS (
                    SELECT 1 FROM master_data_tables t JOIN registry USING (branch_id, master_data_version)
                    WHERE t.table_id = a.table_id
                )
            )
            SELECT r.master_data_version, v.table_id, v.display_name, v.zone,
                   v.status, v.table_session_id, v.version
            FROM registry r LEFT JOIN visible v ON 1 = 1
            ORDER BY v.zone, v.display_order, v.table_id;
            """;
        command.Parameters.AddWithValue("$branchId", branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) { return null; }

        var version = reader.GetString(0);
        var tables = new List<BranchFloorPlanTable>();
        do
        {
            if (reader.IsDBNull(1)) { continue; }
            tables.Add(new(reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6)));
        }
        while (await reader.ReadAsync(cancellationToken));

        return new(version, tables);
    }
}
