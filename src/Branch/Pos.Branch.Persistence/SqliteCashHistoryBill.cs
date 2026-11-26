using System.Text;
using System.Text.Json;

namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private const int MaximumCashHistorySnapshotBytes = 1024 * 1024;

    private static (CashHistoryItem Item, StoreBillTotals? Financials, CashHistoryBill? Bill) ReadCashHistorySnapshot(
        string json, string branchId, CashHistoryItem item)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > MaximumCashHistorySnapshotBytes) { throw InvalidCashBill(); }
            using var document = JsonDocument.Parse(json);
            var root = CashBillObject(document.RootElement);
            var tableId = root.TryGetProperty("tableId", out var table) ? CashBillText(table, 256) : null;
            foreach (var (name, expected) in new[] { ("branchId", branchId), ("tableSessionId", item.TableSessionId) })
            {
                if (root.TryGetProperty(name, out var value) && CashBillText(value, 256) != expected) { throw InvalidCashBill(); }
            }
            var financials = ReadCapturedBillTotals(json, item.AppliedAmount);
            if (!root.TryGetProperty("items", out var capturedItems))
            {
                return (item with { TableId = tableId }, financials, null);
            }
            if (capturedItems.ValueKind != JsonValueKind.Array || financials is null || tableId is null ||
                CashBillText(root.GetProperty("branchId"), 256) != branchId ||
                CashBillText(root.GetProperty("tableSessionId"), 256) != item.TableSessionId) { throw InvalidCashBill(); }
            var lines = new List<CashHistoryBillLine>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawLine in capturedItems.EnumerateArray())
            {
                var line = CashBillObject(rawLine);
                var lineId = CashBillText(line.GetProperty("orderLineId"), 256);
                if (!identities.Add(lineId)) { throw InvalidCashBill(); }
                var options = new List<CashHistoryBillOption>();
                var rawOptions = line.GetProperty("options");
                if (rawOptions.ValueKind != JsonValueKind.Array) { throw InvalidCashBill(); }
                foreach (var rawOption in rawOptions.EnumerateArray())
                {
                    var option = CashBillObject(rawOption);
                    options.Add(new(CashBillText(option.GetProperty("optionId"), 256),
                        CashBillText(option.GetProperty("displayName"), 512, allowLayout: true), CashBillQuantity(option.GetProperty("quantityPerItem")),
                        CashBillAmount(option.GetProperty("unitPrice"))));
                }
                var gross = CashBillAmount(line.GetProperty("grossAmount"));
                var discount = CashBillDiscount(line, "discount", gross);
                var net = CashBillAmount(line.GetProperty("netAmount"));
                if (gross - (discount?.AppliedAmount ?? 0) != net) { throw InvalidCashBill(); }
                var fulfillment = CashBillText(line.GetProperty("fulfillmentType"), 256);
                if (fulfillment is not ("DineIn" or "Takeaway")) { throw InvalidCashBill(); }
                string? comment = null;
                if (line.TryGetProperty("comment", out var rawComment) && rawComment.ValueKind != JsonValueKind.Null)
                {
                    if (rawComment.ValueKind != JsonValueKind.String) { throw InvalidCashBill(); }
                    comment = rawComment.GetString()!;
                    if (comment.Length > 4096 || comment.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                    { throw InvalidCashBill(); }
                }
                lines.Add(new(lineId, CashBillText(line.GetProperty("orderBatchId"), 256),
                    CashBillText(line.GetProperty("sellableItemId"), 256), CashBillText(line.GetProperty("displayName"), 512, allowLayout: true),
                    CashBillQuantity(line.GetProperty("quantity")), CashBillAmount(line.GetProperty("unitPrice")),
                    fulfillment, comment, options.ToArray(), gross, discount, net));
            }
            // Verify recorded monetary relationships only. Historical scale/mode is not in this snapshot.
            // Re-running unit-price or percentage calculations could apply a different rounding policy.
            var itemNet = CashBillAmount(root.GetProperty("itemNetAmount"));
            var billDiscount = CashBillDiscount(root, "billDiscount", itemNet);
            if (lines.Sum(line => line.GrossAmount) != financials.ItemGrossAmount ||
                lines.Sum(line => line.Discount?.AppliedAmount ?? 0) != financials.LineDiscountAmount ||
                lines.Sum(line => line.NetAmount) != itemNet ||
                itemNet != financials.ItemGrossAmount - financials.LineDiscountAmount ||
                (billDiscount?.AppliedAmount ?? 0) != financials.BillDiscountAmount ||
                CashBillAmount(root.GetProperty("serviceChargeBase")) != itemNet - financials.BillDiscountAmount)
            { throw InvalidCashBill(); }
            var bill = new CashHistoryBill(1, branchId, item.TableSessionId, tableId,
                CashBillText(root.GetProperty("requestId"), 256), ReadReportTimestamp(CashBillText(root.GetProperty("capturedAt"), 256)),
                CashBillText(root.GetProperty("masterDataVersion"), 256), CashBillText(root.GetProperty("calculationPolicyVersion"), 256),
                CashBillAmount(root.GetProperty("vatRate")), CashBillAmount(root.GetProperty("serviceChargeRate")), lines.ToArray(), billDiscount);
            return (item with { TableId = tableId, OrderBatchIds = lines.Select(line => line.OrderBatchId).Distinct(StringComparer.Ordinal).ToArray() }, financials, bill);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        { throw new InvalidDataException("Saved cash history bill is unavailable.", error); }
    }

    private static JsonElement CashBillObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
        { throw InvalidCashBill(); }
        return value;
    }

    private static string CashBillText(JsonElement value, int maximum, bool allowLayout = false)
    {
        if (value.ValueKind != JsonValueKind.String) { throw InvalidCashBill(); }
        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || text.Length > maximum ||
            text.Any(character => char.IsControl(character) && !(allowLayout && (character is '\r' or '\n' or '\t')))) { throw InvalidCashBill(); }
        return text;
    }

    private static decimal CashBillAmount(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount) || amount < 0) { throw InvalidCashBill(); }
        return amount;
    }

    private static int CashBillQuantity(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var quantity) || quantity < 1) { throw InvalidCashBill(); }
        return quantity;
    }

    private static CashHistoryDiscount? CashBillDiscount(JsonElement parent, string name, decimal basis)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) { return null; }
        var discount = CashBillObject(value);
        var kind = CashBillText(discount.GetProperty("kind"), 256);
        var requestedValue = CashBillAmount(discount.GetProperty("requestedValue"));
        var requestedAmount = CashBillAmount(discount.GetProperty("requestedAmount"));
        var applied = CashBillAmount(discount.GetProperty("appliedAmount"));
        if (kind is not ("Percentage" or "FixedAmount") || kind == "Percentage" && requestedValue > 100 ||
            applied > requestedAmount || applied > basis) { throw InvalidCashBill(); }
        return new(kind, requestedValue, requestedAmount, applied);
    }

    private static InvalidDataException InvalidCashBill() => new("Saved cash history bill is unavailable.");
}
