using System.Security.Cryptography;
using System.Text.Json;
using Pos.Application.Printing;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<string?> QueueCancellationInspectionAsync(
        string branchId, string cancellationId, DateTimeOffset now, CancellationToken token)
    {
        using var lookup = CreateCommand("""
            SELECT r.table_session_id, s.table_id, r.status,
                   (SELECT print_job_id FROM print_jobs WHERE branch_id = r.branch_id
                    AND request_id = r.cancellation_request_id AND purpose = 'CancellationInspection')
            FROM cancellation_requests r
            JOIN table_sessions s USING (branch_id, table_session_id)
            WHERE r.branch_id = $branch AND r.cancellation_request_id = $request;
            """);
        lookup.Parameters.AddWithValue("$branch", branchId);
        lookup.Parameters.AddWithValue("$request", cancellationId);
        string sessionId;
        string tableId;
        await using (var reader = await lookup.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) { return null; }
            if (!reader.IsDBNull(3)) { return reader.GetString(3); }
            if (reader.GetString(2) != "CancellationRequested") { return null; }
            sessionId = reader.GetString(0);
            tableId = reader.GetString(1);
        }

        using var lines = CreateCommand("""
            SELECT o.display_name, l.requested_quantity
            FROM cancellation_request_lines l JOIN order_lines o USING (branch_id, order_line_id)
            WHERE l.branch_id = $branch AND l.cancellation_request_id = $request
            ORDER BY o.order_line_id;
            """);
        lines.Parameters.AddWithValue("$branch", branchId);
        lines.Parameters.AddWithValue("$request", cancellationId);
        var items = new List<object>();
        await using (var reader = await lines.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                items.Add(new { displayName = reader.GetString(0), requestedQuantity = reader.GetInt32(1) });
            }
        }

        if (items.Count == 0) { return null; }

        var json = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, cancellationRequestId = cancellationId, tableId, items });
        var snapshotId = Guid.NewGuid().ToString("N");
        using var write = CreateCommand("""
            INSERT INTO cancellation_inspection_snapshots VALUES ($snapshot, $branch, $request, $session, 1, $json, $hash);
            UPDATE cancellation_requests SET status = 'ProductionStatusChecking'
            WHERE branch_id = $branch AND cancellation_request_id = $request;
            """);
        write.Parameters.AddWithValue("$snapshot", snapshotId);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$request", cancellationId);
        write.Parameters.AddWithValue("$session", sessionId);
        write.Parameters.AddWithValue("$json", System.Text.Encoding.UTF8.GetString(json));
        write.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(json)));
        await write.ExecuteNonQueryAsync(token);
        var jobId = Guid.NewGuid().ToString("N");
        await AddPrintJobAsync(new(jobId, branchId, cancellationId, TableSessionId.From(sessionId),
            snapshotId, PrintJobPurpose.CancellationInspection, PrintTargetStation.Cashier,
            PersistentPrintJobStatus.PrintQueued, 0, now), token);
        return jobId;
    }
}
