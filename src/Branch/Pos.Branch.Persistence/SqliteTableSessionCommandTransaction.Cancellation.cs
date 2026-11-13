namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed partial class SqliteTableSessionCommandTransaction
{
    public async Task<bool> TryReserveCancellationAsync(
        string branchId, string sessionId, string cancellationId,
        IReadOnlyDictionary<string, int> quantities, CancellationToken cancellationToken)
    {
        if (quantities.Count == 0 || quantities.Values.Any(quantity => quantity <= 0))
        {
            return false;
        }

        using var available = CreateCommand("""
            SELECT line.active_quantity - COALESCE((
                SELECT SUM(reservation.requested_quantity)
                FROM cancellation_request_lines reservation
                JOIN cancellation_requests request USING (branch_id, cancellation_request_id)
                WHERE reservation.branch_id = line.branch_id
                  AND reservation.order_line_id = line.order_line_id
                  AND request.status IN ('CancellationRequested', 'ProductionStatusChecking', 'StatusRecorded')
            ), 0)
            FROM order_lines line
            JOIN table_sessions session USING (branch_id, table_session_id)
            WHERE line.branch_id = $branch AND line.table_session_id = $session
              AND line.order_line_id = $line AND line.status = 'Active' AND session.status = 'Ordering';
            """);
        available.Parameters.AddWithValue("$branch", branchId);
        available.Parameters.AddWithValue("$session", sessionId);
        var lineId = available.Parameters.AddWithValue("$line", "");
        foreach (var (id, quantity) in quantities)
        {
            lineId.Value = id;
            if (await available.ExecuteScalarAsync(cancellationToken) is not long unlocked || quantity > unlocked)
            {
                return false;
            }
        }

        using var request = CreateCommand("""
            INSERT INTO cancellation_requests (branch_id, cancellation_request_id, table_session_id, status)
            VALUES ($branch, $request, $session, 'CancellationRequested');
            """);
        request.Parameters.AddWithValue("$branch", branchId);
        request.Parameters.AddWithValue("$request", cancellationId);
        request.Parameters.AddWithValue("$session", sessionId);
        await request.ExecuteNonQueryAsync(cancellationToken);

        using var reserve = CreateCommand("""
            INSERT INTO cancellation_request_lines (branch_id, cancellation_request_id, order_line_id, requested_quantity)
            VALUES ($branch, $request, $line, $quantity);
            """);
        reserve.Parameters.AddWithValue("$branch", branchId);
        reserve.Parameters.AddWithValue("$request", cancellationId);
        var reservedLine = reserve.Parameters.AddWithValue("$line", "");
        var reservedQuantity = reserve.Parameters.AddWithValue("$quantity", 0);
        foreach (var (id, quantity) in quantities)
        {
            reservedLine.Value = id;
            reservedQuantity.Value = quantity;
            await reserve.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }
}
