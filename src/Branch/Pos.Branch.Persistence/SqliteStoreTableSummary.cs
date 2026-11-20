namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record StoreTableTotals(int OpenTableCount, int AwaitingPaymentTableCount, int PartiallyPaidTableCount);
public sealed record StoreClosingBlockedTable(string TableSessionId, string TableId);

public sealed class StoreClosingBlockedException(IReadOnlyList<StoreClosingBlockedTable> tables)
    : InvalidOperationException("Resolve partially paid tables before closing the shift or store.")
{
    public IReadOnlyList<StoreClosingBlockedTable> Tables { get; } = tables;
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private async Task<StoreTableTotals> ReadStoreTableTotalsAsync(string branchId, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT status, COUNT(*) FROM table_sessions
            WHERE branch_id = $branch AND status IN ('Ordering','PaymentPending','PaymentInProgress','PartiallyPaid')
            GROUP BY status;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        await using var reader = await read.ExecuteReaderAsync(token);
        int open = 0, awaiting = 0, partial = 0;
        while (await reader.ReadAsync(token))
        {
            var count = checked((int)reader.GetInt64(1));
            open = checked(open + count);
            var status = reader.GetString(0);
            if (status != "Ordering") { awaiting = checked(awaiting + count); }
            if (status == "PartiallyPaid") { partial = count; }
        }
        return new(open, awaiting, partial);
    }

    private async Task EnsureNoPartiallyPaidTablesAsync(string branchId, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT table_session_id,table_id FROM table_sessions
            WHERE branch_id = $branch AND status = 'PartiallyPaid'
            ORDER BY table_id,table_session_id;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        await using var reader = await read.ExecuteReaderAsync(token);
        var tables = new List<StoreClosingBlockedTable>();
        while (await reader.ReadAsync(token)) { tables.Add(new(reader.GetString(0), reader.GetString(1))); }
        if (tables.Count > 0) { throw new StoreClosingBlockedException(tables.AsReadOnly()); }
    }
}
