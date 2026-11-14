namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<bool> OpenTestCashCycleAsync(string branchId, string shiftId, string storeId, DateOnly businessDate, CancellationToken token) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).OpenTestCashCycleAsync(branchId, shiftId, storeId, businessDate, cancellation),
            token);

    public Task<TestCashClose?> CloseTestCashCycleAsync(string branchId, string shiftId, decimal openingCash,
        decimal countedCash, CancellationToken token) =>
        ExecuteAtomicallyAsync(async (transaction, cancellation) =>
        {
            var cash = (SqliteTableSessionCommandTransaction)transaction;
            return await cash.CloseTestCashCycleAsync(branchId, shiftId, openingCash, countedCash, cancellation);
        }, token);
}

public sealed record TestCashClose(decimal ExpectedCash, decimal CountedCash, decimal Difference);

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<bool> OpenTestCashCycleAsync(string branchId, string shiftId, string storeId, DateOnly businessDate, CancellationToken token)
    {
        using var command = CreateCommand("""
            INSERT INTO cash_sale_open_shifts (branch_id, shift_id, store_session_id, business_date)
            VALUES ($branch, $shift, $store, $date) ON CONFLICT (branch_id) DO NOTHING;
            """);
        command.Parameters.AddWithValue("$branch", branchId);
        command.Parameters.AddWithValue("$shift", shiftId);
        command.Parameters.AddWithValue("$store", storeId);
        command.Parameters.AddWithValue("$date", businessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    internal async Task<TestCashClose?> CloseTestCashCycleAsync(string branchId, string shiftId, decimal openingCash,
        decimal countedCash, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(countedCash);
        using var close = CreateCommand("""
            DELETE FROM cash_sale_open_shifts WHERE branch_id = $branch AND shift_id = $shift;
            """);
        close.Parameters.AddWithValue("$branch", branchId);
        close.Parameters.AddWithValue("$shift", shiftId);
        var expected = await ReadExpectedTestCashAsync(branchId, openingCash, token);
        if (expected is null || await close.ExecuteNonQueryAsync(token) != 1) { return null; }
        var result = new TestCashClose(expected.Value, countedCash, countedCash - expected.Value);
        using var history = CreateCommand("""
            CREATE TABLE IF NOT EXISTS test_cash_close_history (
                branch_id TEXT NOT NULL,
                shift_id TEXT NOT NULL,
                result_json TEXT NOT NULL CHECK (json_valid(result_json)),
                PRIMARY KEY (branch_id, shift_id)
            );
            INSERT INTO test_cash_close_history VALUES ($branch, $shift, $result);
            """);
        history.Parameters.AddWithValue("$branch", branchId);
        history.Parameters.AddWithValue("$shift", shiftId);
        history.Parameters.AddWithValue("$result", System.Text.Json.JsonSerializer.Serialize(result));
        await history.ExecuteNonQueryAsync(token);
        return result;
    }
}
