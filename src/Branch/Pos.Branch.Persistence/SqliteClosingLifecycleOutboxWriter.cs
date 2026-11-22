using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

internal static class SqliteClosingLifecycleOutboxWriter
{
    internal static async Task EnqueueAsync(SqliteConnection connection, SqliteTransaction transaction,
        string? branchId, string? requestId, CancellationToken token)
    {
        connection.CreateFunction<string, string>("closing_context_utc", value =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), isDeterministic: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO closing_report_context_outbox
                (context_id,branch_id,request_id,payload_json,created_at,available_at)
            SELECT 'lifecycle:' || parent.report_id, frozen.branch_id, frozen.request_id,
                json_object('schemaVersion',1,'contextId','lifecycle:' || parent.report_id,
                    'reportId',parent.report_id,'branchId',frozen.branch_id,'requestId',frozen.request_id,
                    'context',json(frozen.context_json)),
                closing_context_utc(parent.created_at),closing_context_utc(parent.created_at)
            FROM closing_report_lifecycle_snapshots frozen
            JOIN closing_report_outbox parent ON parent.branch_id=frozen.branch_id AND parent.request_id=frozen.request_id
            WHERE ($branch IS NULL OR frozen.branch_id=$branch) AND ($request IS NULL OR frozen.request_id=$request)
            ORDER BY parent.sequence
            ON CONFLICT(branch_id,request_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$branch", (object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$request", (object?)requestId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }
}
