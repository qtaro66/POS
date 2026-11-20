using System.Text.Json;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record StoreCashTotals(int ReceiptCount, decimal AppliedCashAmount, StoreBillTotals? Financials = null);
public sealed record StoreBillTotals(decimal ItemGrossAmount, decimal LineDiscountAmount, decimal BillDiscountAmount,
    decimal ServiceChargeAmount, decimal TotalBeforeRounding, decimal RoundingAdjustment, decimal AmountDue,
    decimal VatInclusiveAmount, decimal AmountBeforeVat)
{
    public StoreBillTotals Add(StoreBillTotals other) => new(ItemGrossAmount + other.ItemGrossAmount,
        LineDiscountAmount + other.LineDiscountAmount, BillDiscountAmount + other.BillDiscountAmount,
        ServiceChargeAmount + other.ServiceChargeAmount, TotalBeforeRounding + other.TotalBeforeRounding,
        RoundingAdjustment + other.RoundingAdjustment, AmountDue + other.AmountDue,
        VatInclusiveAmount + other.VatInclusiveAmount, AmountBeforeVat + other.AmountBeforeVat);
}
public sealed record StoreCashSummary(string Source, StoreCashTotals Store, StoreCashTotals? Shift);

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private async Task<StoreCashSummary> ReadStoreCashSummaryAsync(string branchId, string storeId,
        string? shiftId, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT result_json, bill_snapshot_json FROM cash_sales
            WHERE branch_id = $branch AND json_extract(result_json, '$.StoreSessionId') = $store;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$store", storeId);
        await using var reader = await read.ExecuteReaderAsync(token);
        int storeCount = 0, shiftCount = 0;
        decimal storeCash = 0, shiftCash = 0;
        StoreBillTotals? storeBills = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
        StoreBillTotals? shiftBills = storeBills;
        while (await reader.ReadAsync(token))
        {
            var sale = JsonSerializer.Deserialize<CashSaleResult>(reader.GetString(0))!;
            storeCount++;
            storeCash += sale.AppliedAmount;
            var bill = ReadCapturedBillTotals(reader.GetString(1), sale.AppliedAmount);
            storeBills = storeBills is not null && bill is not null ? storeBills.Add(bill) : null;
            if (sale.ShiftId == shiftId)
            {
                shiftCount++;
                shiftCash += sale.AppliedAmount;
                shiftBills = shiftBills is not null && bill is not null ? shiftBills.Add(bill) : null;
            }
        }
        return new("CashSales", new(storeCount, storeCash, storeCount == 0 ? null : storeBills),
            shiftId is null ? null : new(shiftCount, shiftCash, shiftCount == 0 ? null : shiftBills));
    }

    private static StoreBillTotals? ReadCapturedBillTotals(string json, decimal appliedCash)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) { return null; }
        var names = new[] { "itemGrossAmount", "lineDiscountAmount", "serviceChargeAmount", "totalBeforeRounding",
            "roundingAdjustment", "netTotal", "vatInclusiveAmount", "amountBeforeVat" };
        var amounts = new decimal[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            if (!root.TryGetProperty(names[index], out var value) || value.ValueKind != JsonValueKind.Number ||
                !value.TryGetDecimal(out amounts[index])) { return null; }
        }
        var hasDiscount = root.TryGetProperty("billDiscount", out var discount);
        decimal billDiscount = 0;
        if (hasDiscount && discount.ValueKind != JsonValueKind.Null &&
            (discount.ValueKind != JsonValueKind.Object || !discount.TryGetProperty("appliedAmount", out var amount) ||
                amount.ValueKind != JsonValueKind.Number || !amount.TryGetDecimal(out billDiscount))) { return null; }
        var result = new StoreBillTotals(amounts[0], amounts[1], billDiscount, amounts[2], amounts[3], amounts[4],
            amounts[5], amounts[6], amounts[7]);
        if (result.ItemGrossAmount < 0 || result.LineDiscountAmount < 0 || result.BillDiscountAmount < 0 ||
            result.ServiceChargeAmount < 0 || result.TotalBeforeRounding < 0 || result.VatInclusiveAmount < 0 ||
            result.AmountBeforeVat < 0 || result.AmountDue != appliedCash || result.RoundingAdjustment > 0 ||
            result.ItemGrossAmount - result.LineDiscountAmount - result.BillDiscountAmount + result.ServiceChargeAmount != result.TotalBeforeRounding ||
            result.TotalBeforeRounding + result.RoundingAdjustment != result.AmountDue ||
            result.AmountBeforeVat + result.VatInclusiveAmount != result.TotalBeforeRounding) { return null; }
        return result;
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
