namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<string?> RenderTestReceiptAsync(string branchId, string receiptNumber, CancellationToken token) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).RenderTestReceiptAsync(branchId, receiptNumber, cancellation), token);

    public Task<bool> OpenTestCashCycleAsync(string branchId, string shiftId, string storeId, DateOnly businessDate, decimal openingCash, CancellationToken token) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).OpenTestCashCycleAsync(branchId, shiftId, storeId, businessDate, openingCash, cancellation),
            token);

    public Task<TestCashClose?> CloseTestCashCycleAsync(string branchId, string shiftId,
        decimal countedCash, CancellationToken token, string? employeeId = null, DateTimeOffset? closedAt = null) =>
        ExecuteAtomicallyAsync(async (transaction, cancellation) =>
        {
            var cash = (SqliteTableSessionCommandTransaction)transaction;
            return await cash.CloseTestCashCycleAsync(branchId, shiftId, countedCash, cancellation, employeeId, closedAt);
        }, token);
}

public sealed record TestCashClose(decimal ExpectedCash, decimal CountedCash, decimal Difference, string? EmployeeId = null, DateTimeOffset? ClosedAt = null);

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<string?> RenderTestReceiptAsync(string branchId, string receiptNumber, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT sale.result_json, sale.bill_snapshot_json
            FROM simulated_cash_receipt_jobs job JOIN cash_sales sale USING (branch_id, request_id)
            WHERE job.branch_id = $branch AND job.receipt_number = $receipt;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$receipt", receiptNumber);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { return null; }

        var sale = System.Text.Json.JsonSerializer.Deserialize<CashSaleResult>(reader.GetString(0))!;
        using var bill = System.Text.Json.JsonDocument.Parse(reader.GetString(1));
        var lines = new List<string> { "ใบเสร็จทดสอบ " + sale.ReceiptNumber, sale.BusinessDate };
        foreach (var item in bill.RootElement.GetProperty("items").EnumerateArray())
        {
            lines.Add(item.GetProperty("displayName").GetString() ?? "");
        }

        lines.Add(FormattableString.Invariant($"ยอดชำระ {sale.AppliedAmount:0.00} · รับ {sale.TenderedAmount:0.00} · ทอน {sale.ChangeAmount:0.00}"));
        return string.Join(Environment.NewLine, lines);
    }

    internal async Task<bool> OpenTestCashCycleAsync(string branchId, string shiftId, string storeId, DateOnly businessDate, decimal openingCash, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(openingCash);
        using var command = CreateCommand("""
            INSERT INTO cash_sale_open_shifts (branch_id, shift_id, store_session_id, business_date)
            VALUES ($branch, $shift, $store, $date) ON CONFLICT (branch_id) DO NOTHING;
            """);
        command.Parameters.AddWithValue("$branch", branchId);
        command.Parameters.AddWithValue("$shift", shiftId);
        command.Parameters.AddWithValue("$store", storeId);
        command.Parameters.AddWithValue("$date", businessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync(token) != 1) { return false; }
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS test_cash_open_history (
                branch_id TEXT NOT NULL, shift_id TEXT NOT NULL, opening_cash TEXT NOT NULL,
                PRIMARY KEY (branch_id, shift_id)
            );
            INSERT INTO test_cash_open_history VALUES ($branch, $shift, $cash);
            """;
        command.Parameters.AddWithValue("$cash", openingCash.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(token);
        return true;
    }

    internal async Task<TestCashClose?> CloseTestCashCycleAsync(string branchId, string shiftId,
        decimal countedCash, CancellationToken token, string? employeeId = null, DateTimeOffset? closedAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(countedCash);
        using var close = CreateCommand("""
            DELETE FROM cash_sale_open_shifts WHERE branch_id = $branch AND shift_id = $shift;
            """);
        close.Parameters.AddWithValue("$branch", branchId);
        close.Parameters.AddWithValue("$shift", shiftId);
        using var opening = CreateCommand("""
            SELECT opening_cash FROM test_cash_open_history WHERE branch_id = $branch AND shift_id = $shift;
            """);
        opening.Parameters.AddWithValue("$branch", branchId);
        opening.Parameters.AddWithValue("$shift", shiftId);
        if (await opening.ExecuteScalarAsync(token) is not string value) { return null; }
        var expected = await ReadExpectedTestCashAsync(branchId,
            decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), token);
        if (expected is null || await close.ExecuteNonQueryAsync(token) != 1) { return null; }
        var result = new TestCashClose(expected.Value, countedCash, countedCash - expected.Value, employeeId, closedAt);
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
