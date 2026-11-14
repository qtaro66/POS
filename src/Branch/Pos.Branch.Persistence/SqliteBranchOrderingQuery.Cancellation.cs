using Pos.Application.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteBranchOrderingQuery
{
    public async Task<IReadOnlyList<BranchCancellationQueueEntry>> CancellationQueueAsync(
        string branchId, CancellationToken cancellationToken)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH pending AS (
                SELECT branch_id, cancellation_request_id, table_session_id, status
                FROM cancellation_requests
                WHERE branch_id = $branch AND status IN ('CancellationRequested', 'ProductionStatusChecking', 'StatusRecorded')
                ORDER BY cancellation_request_id
                LIMIT 100
            )
            SELECT p.cancellation_request_id, p.table_session_id, p.status,
                   r.order_line_id, l.display_name, r.requested_quantity, c.not_started, c.in_progress, c.completed, c.not_applicable, n.reason
            FROM pending p
            LEFT JOIN cancellation_request_lines r USING (branch_id, cancellation_request_id)
            LEFT JOIN order_lines l ON l.branch_id = r.branch_id AND l.order_line_id = r.order_line_id
            LEFT JOIN cancellation_production_checks c ON c.branch_id = r.branch_id
                AND c.cancellation_request_id = r.cancellation_request_id AND c.order_line_id = r.order_line_id
            LEFT JOIN cancellation_request_reasons n ON n.branch_id = p.branch_id AND n.cancellation_request_id = p.cancellation_request_id
            ORDER BY p.cancellation_request_id, r.order_line_id;
            """;
        command.Parameters.AddWithValue("$branch", branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<BranchCancellationQueueEntry>();
        List<BranchCancellationQueueLine> lines = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (entries.Count == 0 || entries[^1].CancellationRequestId != id)
            {
                lines = [];
                entries.Add(new(id, reader.GetString(1), reader.GetString(2), lines, reader.IsDBNull(10) ? null : reader.GetString(10)));
            }

            if (!reader.IsDBNull(3))
            {
                lines.Add(new(reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.IsDBNull(6) ? null :
                    new(reader.GetString(3), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9))));
            }
        }

        return entries;
    }
}
