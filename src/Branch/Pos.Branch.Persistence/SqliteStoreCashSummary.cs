using System.Text.Json;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record StoreCashTotals(int ReceiptCount, decimal AppliedCashAmount);
public sealed record StoreCashSummary(string Source, StoreCashTotals Store, StoreCashTotals? Shift);

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private async Task<StoreCashSummary> ReadStoreCashSummaryAsync(string branchId, string storeId,
        string? shiftId, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT result_json FROM cash_sales
            WHERE branch_id = $branch AND json_extract(result_json, '$.StoreSessionId') = $store;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$store", storeId);
        await using var reader = await read.ExecuteReaderAsync(token);
        int storeCount = 0, shiftCount = 0;
        decimal storeCash = 0, shiftCash = 0;
        while (await reader.ReadAsync(token))
        {
            var sale = JsonSerializer.Deserialize<CashSaleResult>(reader.GetString(0))!;
            storeCount++;
            storeCash += sale.AppliedAmount;
            if (sale.ShiftId == shiftId)
            {
                shiftCount++;
                shiftCash += sale.AppliedAmount;
            }
        }
        return new("CashSales", new(storeCount, storeCash), shiftId is null ? null : new(shiftCount, shiftCash));
    }

    private async Task CaptureStoreCashSummaryAsync(string branchId, StoreLifecycleCommand command, CancellationToken token)
    {
        var summary = await ReadStoreCashSummaryAsync(branchId, command.StoreSessionId, command.ShiftId, token);
        using var write = CreateCommand("""
            INSERT INTO store_lifecycle_cash_snapshots(branch_id,request_id,store_session_id,shift_id,summary_json)
                VALUES ($branch,$request,$store,$shift,$summary);
            """);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$request", command.RequestId);
        write.Parameters.AddWithValue("$store", command.StoreSessionId);
        write.Parameters.AddWithValue("$shift", command.ShiftId);
        write.Parameters.AddWithValue("$summary", JsonSerializer.Serialize(summary));
        await write.ExecuteNonQueryAsync(token);
    }
}
