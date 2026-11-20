using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

internal static class SqliteClosingReportOutboxWriter
{
    internal static async Task EnqueueAsync(SqliteConnection connection, SqliteTransaction transaction,
        string? branchId, string? requestId, CancellationToken token)
    {
        using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = """
            INSERT INTO closing_report_outbox
                (report_id,branch_id,request_id,operation,payload_json,created_at,available_at)
            SELECT 'closing:' || hex(history.branch_id) || ':' || hex(history.request_id),
                history.branch_id,history.request_id,
                CASE json_extract(history.command_json,'$.Action') WHEN 2 THEN 'CloseShift' ELSE 'CloseStore' END,
                json_object(
                    'schemaVersion',1,
                    'reportId','closing:' || hex(history.branch_id) || ':' || hex(history.request_id),
                    'branchId',history.branch_id,'requestId',history.request_id,
                    'storeSessionId',history.store_session_id,
                    'shiftId',json_extract(history.command_json,'$.ShiftId'),
                    'operation',CASE json_extract(history.command_json,'$.Action') WHEN 2 THEN 'CloseShift' ELSE 'CloseStore' END,
                    'businessDate',json_extract(history.result_json,'$.BusinessDate'),
                    'closing',json_extract(history.result_json,'$.Closing'),
                    'cashSummary',json(snapshot.summary_json)),
                json_extract(history.command_json,'$.Context.OccurredAt'),
                json_extract(history.command_json,'$.Context.OccurredAt')
            FROM store_lifecycle_commands history
            LEFT JOIN store_lifecycle_cash_snapshots snapshot
                ON snapshot.branch_id=history.branch_id AND snapshot.request_id=history.request_id
            WHERE json_extract(history.command_json,'$.Action') IN (2,3)
                AND json_type(history.result_json,'$.Closing')='object'
                AND ($branch IS NULL OR history.branch_id=$branch)
                AND ($request IS NULL OR history.request_id=$request)
            ORDER BY history.sequence
            ON CONFLICT(branch_id,request_id) DO NOTHING;
            """;
        write.Parameters.AddWithValue("$branch", (object?)branchId ?? DBNull.Value);
        write.Parameters.AddWithValue("$request", (object?)requestId ?? DBNull.Value);
        await write.ExecuteNonQueryAsync(token);
    }
}
