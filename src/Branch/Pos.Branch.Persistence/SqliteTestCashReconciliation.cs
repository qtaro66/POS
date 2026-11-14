using System.Text.Json;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<decimal?> ReadExpectedTestCashAsync(string branchId, decimal openingCash, CancellationToken token) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadExpectedTestCashAsync(branchId, openingCash, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<decimal?> ReadExpectedTestCashAsync(string branchId, decimal openingCash, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(openingCash);
        using var shift = CreateCommand("SELECT store_session_id FROM cash_sale_open_shifts WHERE branch_id = $branch;");
        shift.Parameters.AddWithValue("$branch", branchId);
        if (await shift.ExecuteScalarAsync(token) is not string storeSessionId) { return null; }

        using var sales = CreateCommand("""
            SELECT result_json FROM cash_sales
            WHERE branch_id = $branch AND json_extract(result_json, '$.StoreSessionId') = $store;
            """);
        sales.Parameters.AddWithValue("$branch", branchId);
        sales.Parameters.AddWithValue("$store", storeSessionId);
        await using var reader = await sales.ExecuteReaderAsync(token);
        var expected = openingCash;
        while (await reader.ReadAsync(token))
        {
            expected += JsonSerializer.Deserialize<CashSaleResult>(reader.GetString(0))!.AppliedAmount;
        }

        return expected;
    }
}
