using System.Text.Json;
using Pos.Application.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<string?> ConfirmCancellationAsync(string branchId, string requestId, string employeeId,
        DateTimeOffset now, IReadOnlyList<CancellationProductionCheck> quantities, CancellationToken token)
    {
        var selected = new Dictionary<string, CancellationProductionCheck>(StringComparer.Ordinal);
        foreach (var line in quantities)
        {
            if (line is null || string.IsNullOrWhiteSpace(line.OrderLineId) || !selected.TryAdd(line.OrderLineId, line) ||
                line.NotStarted < 0 || line.InProgress < 0 || line.Completed < 0 || line.NotApplicable < 0)
            {
                return null;
            }
        }

        using var lookup = CreateCommand("""
            SELECT r.status, r.table_session_id, s.status, event.details_json
            FROM cancellation_requests r JOIN table_sessions s USING (branch_id, table_session_id)
            LEFT JOIN cancellation_confirmations event USING (branch_id, cancellation_request_id)
            WHERE r.branch_id = $branch AND r.cancellation_request_id = $request;
            """);
        lookup.Parameters.AddWithValue("$branch", branchId);
        lookup.Parameters.AddWithValue("$request", requestId);
        string sessionId;
        var normalized = selected.Values.OrderBy(line => line.OrderLineId, StringComparer.Ordinal).ToArray();
        await using (var reader = await lookup.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) { return null; }
            var status = reader.GetString(0);
            if (status is "Confirmed" or "PartiallyConfirmed")
            {
                if (reader.IsDBNull(3)) { return null; }
                var previous = JsonSerializer.Deserialize<ConfirmationLine[]>(reader.GetString(3));
                return previous is not null && previous.Select(line => line.Cancelled).SequenceEqual(normalized) ? status : null;
            }

            if (status != "StatusRecorded" || reader.GetString(2) != "Ordering") { return null; }
            sessionId = reader.GetString(1);
        }

        using var read = CreateCommand("""
            SELECT l.order_line_id, l.requested_quantity, c.not_started, c.in_progress, c.completed,
                   c.not_applicable, o.active_quantity
            FROM cancellation_request_lines l
            JOIN cancellation_production_checks c USING (branch_id, cancellation_request_id, order_line_id)
            JOIN order_lines o USING (branch_id, order_line_id)
            WHERE l.branch_id = $branch AND l.cancellation_request_id = $request AND o.table_session_id = $session
            ORDER BY l.order_line_id;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$request", requestId);
        read.Parameters.AddWithValue("$session", sessionId);
        var evidence = new List<ConfirmationLine>();
        long total = 0;
        var partial = false;
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var id = reader.GetString(0);
                var requested = reader.GetInt32(1);
                var checkedCounts = new CancellationProductionCheck(id, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
                if (!selected.TryGetValue(id, out var cancelled) ||
                    cancelled.NotStarted > checkedCounts.NotStarted || cancelled.InProgress > checkedCounts.InProgress ||
                    cancelled.Completed > checkedCounts.Completed || cancelled.NotApplicable > checkedCounts.NotApplicable)
                {
                    return null;
                }

                var amount = Quantity(cancelled);
                if (amount > reader.GetInt32(6) || amount > requested) { return null; }
                total += amount;
                partial |= amount != requested;
                evidence.Add(new(checkedCounts, cancelled, requested, requested - amount));
            }
        }

        if (total == 0 || evidence.Count != selected.Count) { return null; }
        foreach (var line in evidence)
        {
            var amount = Quantity(line.Cancelled);
            if (amount == 0) { continue; }
            using var update = CreateCommand("""
                UPDATE order_lines SET active_quantity = active_quantity - $quantity,
                    status = CASE WHEN active_quantity = $quantity THEN 'Cancelled' ELSE 'Active' END
                WHERE branch_id = $branch AND table_session_id = $session AND order_line_id = $line;
                """);
            update.Parameters.AddWithValue("$branch", branchId);
            update.Parameters.AddWithValue("$session", sessionId);
            update.Parameters.AddWithValue("$line", line.Cancelled.OrderLineId);
            update.Parameters.AddWithValue("$quantity", amount);
            await update.ExecuteNonQueryAsync(token);
        }

        var outcome = partial ? "PartiallyConfirmed" : "Confirmed";
        using var write = CreateCommand("""
            INSERT INTO cancellation_confirmations VALUES ($branch, $request, $employee, $now, $json);
            UPDATE cancellation_requests SET status = $status
            WHERE branch_id = $branch AND cancellation_request_id = $request;
            UPDATE table_sessions SET version = version + 1
            WHERE branch_id = $branch AND table_session_id = $session;
            """);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$request", requestId);
        write.Parameters.AddWithValue("$session", sessionId);
        write.Parameters.AddWithValue("$employee", employeeId);
        write.Parameters.AddWithValue("$now", FormatTimestamp(now));
        write.Parameters.AddWithValue("$status", outcome);
        write.Parameters.AddWithValue("$json", JsonSerializer.Serialize(evidence));
        await write.ExecuteNonQueryAsync(token);
        return outcome;
    }

    private static long Quantity(CancellationProductionCheck line) =>
        (long)line.NotStarted + line.InProgress + line.Completed + line.NotApplicable;

    private sealed record ConfirmationLine(
        CancellationProductionCheck Checked, CancellationProductionCheck Cancelled, int RequestedQuantity, long NotCancelledQuantity);
}
