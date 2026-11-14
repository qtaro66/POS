using Pos.Application.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<bool> RejectCancellationAsync(string branchId, string requestId, CancellationToken token)
    {
        using var command = CreateCommand("""
            UPDATE cancellation_requests SET status = 'Rejected'
            WHERE branch_id = $branch AND cancellation_request_id = $request AND status = 'StatusRecorded'
              AND EXISTS (
                SELECT 1 FROM table_sessions s
                WHERE s.branch_id = cancellation_requests.branch_id
                  AND s.table_session_id = cancellation_requests.table_session_id AND s.status = 'Ordering');
            """);
        command.Parameters.AddWithValue("$branch", branchId);
        command.Parameters.AddWithValue("$request", requestId);
        if (await command.ExecuteNonQueryAsync(token) == 1) { return true; }

        command.CommandText = """
            SELECT 1 FROM cancellation_requests
            WHERE branch_id = $branch AND cancellation_request_id = $request AND status = 'Rejected';
            """;
        return await command.ExecuteScalarAsync(token) is not null;
    }

    internal async Task<bool> RecordCancellationProductionAsync(string branchId, string requestId, string employeeId,
        DateTimeOffset now, IReadOnlyList<CancellationProductionCheck> checks, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT r.status, l.order_line_id, l.requested_quantity, o.production_station_id,
                   c.not_started, c.in_progress, c.completed, c.not_applicable
            FROM cancellation_requests r
            JOIN cancellation_request_lines l USING (branch_id, cancellation_request_id)
            JOIN order_lines o USING (branch_id, order_line_id)
            LEFT JOIN cancellation_production_checks c USING (branch_id, cancellation_request_id, order_line_id)
            WHERE r.branch_id = $branch AND r.cancellation_request_id = $request
              AND r.status IN ('ProductionStatusChecking', 'StatusRecorded');
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$request", requestId);
        var submitted = new Dictionary<string, CancellationProductionCheck>(StringComparer.Ordinal);
        foreach (var check in checks)
        {
            if (check is null || string.IsNullOrWhiteSpace(check.OrderLineId) || !submitted.TryAdd(check.OrderLineId, check) ||
                check.NotStarted < 0 || check.InProgress < 0 || check.Completed < 0 || check.NotApplicable < 0)
            {
                return false;
            }
        }

        var matched = 0;
        var replay = false;
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                matched++;
                var id = reader.GetString(1);
                var quantity = reader.GetInt32(2);
                if (!submitted.TryGetValue(id, out var check) ||
                    (long)check.NotStarted + check.InProgress + check.Completed + check.NotApplicable != quantity ||
                    (reader.IsDBNull(3) ? check.NotApplicable != quantity : check.NotApplicable != 0))
                {
                    return false;
                }

                replay = reader.GetString(0) == "StatusRecorded";
                if (replay && (reader.IsDBNull(4) || check != new CancellationProductionCheck(
                    id, reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7))))
                {
                    return false;
                }
            }
        }

        if (matched == 0 || matched != submitted.Count) { return false; }
        if (replay) { return true; }

        foreach (var check in checks)
        {
            using var insert = CreateCommand("""
                INSERT INTO cancellation_production_checks
                VALUES ($branch, $request, $line, $notStarted, $inProgress, $completed, $notApplicable, $employee, $now);
                """);
            insert.Parameters.AddWithValue("$branch", branchId);
            insert.Parameters.AddWithValue("$request", requestId);
            insert.Parameters.AddWithValue("$line", check.OrderLineId);
            insert.Parameters.AddWithValue("$notStarted", check.NotStarted);
            insert.Parameters.AddWithValue("$inProgress", check.InProgress);
            insert.Parameters.AddWithValue("$completed", check.Completed);
            insert.Parameters.AddWithValue("$notApplicable", check.NotApplicable);
            insert.Parameters.AddWithValue("$employee", employeeId);
            insert.Parameters.AddWithValue("$now", FormatTimestamp(now));
            await insert.ExecuteNonQueryAsync(token);
        }

        using var update = CreateCommand("""
            UPDATE cancellation_requests SET status = 'StatusRecorded'
            WHERE branch_id = $branch AND cancellation_request_id = $request;
            """);
        update.Parameters.AddWithValue("$branch", branchId);
        update.Parameters.AddWithValue("$request", requestId);
        await update.ExecuteNonQueryAsync(token);
        return true;
    }
}
