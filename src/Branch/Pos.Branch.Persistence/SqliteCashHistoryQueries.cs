using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<CashHistoryPage> ReadCashHistoryPageAsync(string branchId, DateTimeOffset now, CashHistoryQuery query,
        CancellationToken token = default) => ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadCashHistoryPageAsync(branchId, now, query, cancellation), token);
    public Task<CashHistoryDetail?> ReadCashHistoryDetailAsync(string branchId, string requestId, DateTimeOffset now,
        CancellationToken token = default) => ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadCashHistoryDetailAsync(branchId, requestId, now, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private sealed record CashHistoryCursor(int Version, string BranchId, long SchemaCookie, string? RequestedBusinessDate,
        string BusinessDate, string? ReceiptNumber, string? TableSessionId, string PaymentMethod, string Status,
        int Limit, long HighWater, long Before, string? TableId, string? OrderBatchId);
    private const string CashHistoryColumns = "sale.rowid,sale.request_id,sale.table_session_id,sale.result_json,job.receipt_number,job.queued_at,sale.bill_snapshot_json";
    private const string CashHistoryJoin = "cash_sales sale LEFT JOIN simulated_cash_receipt_jobs job ON job.branch_id=sale.branch_id AND job.request_id=sale.request_id";

    internal async Task<CashHistoryPage> ReadCashHistoryPageAsync(string branchId, DateTimeOffset now,
        CashHistoryQuery query, CancellationToken token)
    {
        ValidateReportId(branchId); RequireClosingReportUtc(now); ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 50 || query.PaymentMethod is not ("All" or "Cash") || query.Status is not ("All" or "Completed"))
        { throw new ArgumentException("Invalid cash history query."); }
        if (query.BusinessDate is not null) { ValidateCashHistoryDate(query.BusinessDate); }
        if (query.TableSessionId is not null) { ValidateReportId(query.TableSessionId); }
        if (query.TableId is not null) { ValidateReportId(query.TableId); }
        if (query.OrderBatchId is not null) { ValidateReportId(query.OrderBatchId); }
        if (query.ReceiptNumber is not null)
        {
            ValidateReportId(query.ReceiptNumber);
            if (query.ReceiptNumber.Length > 128) { throw new ArgumentException("Invalid cash history receipt filter."); }
        }
        using var schema = CreateCommand("PRAGMA schema_version;");
        var schemaCookie = Convert.ToInt64(await schema.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        var cursor = query.Cursor is null ? null : DecodeCashHistoryCursor(query.Cursor, branchId, query, schemaCookie);
        var date = cursor?.BusinessDate ?? query.BusinessDate;
        if (date is null)
        {
            date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        using var integrity = CreateCommand($"SELECT EXISTS(SELECT 1 FROM {CashHistoryJoin} WHERE sale.branch_id=$branch AND job.request_id IS NULL);");
        integrity.Parameters.AddWithValue("$branch", branchId);
        if (Convert.ToInt64(await integrity.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0)
        { throw new InvalidDataException("Saved cash history is unavailable."); }
        long highWater;
        if (cursor is not null) { highWater = cursor.HighWater; }
        else
        {
            using var maximum = CreateCommand("SELECT COALESCE(MAX(rowid),0) FROM cash_sales WHERE branch_id=$branch;");
            maximum.Parameters.AddWithValue("$branch", branchId);
            highWater = Convert.ToInt64(await maximum.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        }
        using var read = CreateCommand($"""
            SELECT {CashHistoryColumns} FROM {CashHistoryJoin}
            WHERE sale.branch_id=$branch AND sale.rowid<=$high AND ($before IS NULL OR sale.rowid<$before)
                AND json_extract(sale.result_json,'$.BusinessDate')=$day
                AND ($receipt IS NULL OR instr(json_extract(sale.result_json,'$.ReceiptNumber'),$receipt)>0)
                AND ($session IS NULL OR sale.table_session_id=$session)
                AND ($table IS NULL OR json_extract(sale.bill_snapshot_json,'$.tableId')=$table)
                AND ($batch IS NULL OR EXISTS (SELECT 1 FROM json_each(sale.bill_snapshot_json,'$.items') item
                    WHERE json_extract(item.value,'$.orderBatchId')=$batch))
            ORDER BY sale.rowid DESC LIMIT $limit;
            """);
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$high", highWater);
        read.Parameters.AddWithValue("$before", (object?)cursor?.Before ?? DBNull.Value); read.Parameters.AddWithValue("$day", date);
        read.Parameters.AddWithValue("$receipt", (object?)query.ReceiptNumber ?? DBNull.Value);
        read.Parameters.AddWithValue("$session", (object?)query.TableSessionId ?? DBNull.Value); read.Parameters.AddWithValue("$limit", query.Limit + 1);
        read.Parameters.AddWithValue("$table", (object?)query.TableId ?? DBNull.Value);
        read.Parameters.AddWithValue("$batch", (object?)query.OrderBatchId ?? DBNull.Value);
        var rows = new List<(long Sequence, CashHistoryItem Item)>();
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var captured = ReadCashHistorySnapshot(reader.GetString(6), branchId, ReadCashHistoryItem(reader));
                rows.Add((reader.GetInt64(0), captured.Item));
            }
        }
        string? next = null;
        if (rows.Count > query.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = EncodeCashHistoryCursor(new(2, branchId, schemaCookie, query.BusinessDate, date, query.ReceiptNumber,
                query.TableSessionId, query.PaymentMethod, query.Status, query.Limit, highWater, rows[^1].Sequence, query.TableId, query.OrderBatchId));
        }
        return new(1, branchId, now, date, rows.Select(row => row.Item).ToArray(), next);
    }

    internal async Task<CashHistoryDetail?> ReadCashHistoryDetailAsync(string branchId, string requestId,
        DateTimeOffset now, CancellationToken token)
    {
        ValidateReportId(branchId); ValidateReportId(requestId); RequireClosingReportUtc(now);
        using var read = CreateCommand($"SELECT {CashHistoryColumns} FROM {CashHistoryJoin} WHERE sale.branch_id=$branch AND sale.request_id=$request;");
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$request", requestId);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { return null; }
        var captured = ReadCashHistorySnapshot(reader.GetString(6), branchId, ReadCashHistoryItem(reader));
        return new(1, branchId, now, captured.Item, captured.Financials, captured.Bill);
    }

    private static CashHistoryItem ReadCashHistoryItem(SqliteDataReader reader)
    {
        try
        {
            using var document = JsonDocument.Parse(reader.GetString(3));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
            { throw new InvalidDataException("Saved cash history is ambiguous."); }
            foreach (var name in new[] { "TableSessionId", "EmployeeId", "DeviceId", "ShiftId", "StoreSessionId", "BusinessDate", "ReceiptNumber", "PrintStatus" })
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                { throw new InvalidDataException("Saved cash history identity is incomplete."); }
            }
            foreach (var name in new[] { "TenderedAmount", "AppliedAmount", "ChangeAmount" })
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out _))
                { throw new InvalidDataException("Saved cash history payment is incomplete."); }
            }
            if (!root.TryGetProperty("ExpectedVersion", out var expected) || expected.ValueKind != JsonValueKind.Number || !expected.TryGetInt64(out _))
            { throw new InvalidDataException("Saved cash history version is incomplete."); }
            var sale = root.Deserialize<CashSaleResult>()
                ?? throw new InvalidDataException("Saved cash history is unavailable.");
            foreach (var id in new[] { reader.GetString(1), sale.TableSessionId, sale.ReceiptNumber, sale.StoreSessionId, sale.ShiftId, sale.EmployeeId, sale.DeviceId })
            { ValidateReportId(id); }
            ValidateCashHistoryDate(sale.BusinessDate);
            if (reader.IsDBNull(4) || reader.IsDBNull(5) || reader.GetString(2) != sale.TableSessionId || reader.GetString(4) != sale.ReceiptNumber ||
                sale.ExpectedVersion <= 0 || sale.AppliedAmount < 0 || sale.TenderedAmount < sale.AppliedAmount || sale.ChangeAmount < 0 ||
                sale.TenderedAmount - sale.AppliedAmount != sale.ChangeAmount || sale.PrintStatus != "QueuedForSimulation")
            { throw new InvalidDataException("Saved cash history identity or payment is inconsistent."); }
            return new(reader.GetString(1), sale.TableSessionId, sale.ReceiptNumber, sale.BusinessDate, sale.StoreSessionId,
                sale.ShiftId, sale.EmployeeId, sale.DeviceId, ReadReportTimestamp(reader.GetString(5)), "Cash", "Completed",
                sale.TenderedAmount, sale.AppliedAmount, sale.ChangeAmount, sale.PrintStatus);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Saved cash history is unavailable.", error); }
    }
    private static void ValidateCashHistoryDate(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        { throw new ArgumentException("Invalid cash history business date."); }
    }
    private static string EncodeCashHistoryCursor(CashHistoryCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static CashHistoryCursor DecodeCashHistoryCursor(string value, string branch, CashHistoryQuery query, long schemaCookie)
    {
        try
        {
            if (value.Length is < 1 or > 4096 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            { throw new ArgumentException("Invalid cash history cursor."); }
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
            using var document = JsonDocument.Parse(bytes);
            var names = new HashSet<string>(["Version", "BranchId", "SchemaCookie", "RequestedBusinessDate", "BusinessDate", "ReceiptNumber", "TableSessionId", "PaymentMethod", "Status", "Limit", "HighWater", "Before", "TableId", "OrderBatchId"], StringComparer.Ordinal);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != names.Count ||
                document.RootElement.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != names.Count ||
                document.RootElement.EnumerateObject().Any(property => !names.Contains(property.Name)))
            { throw new ArgumentException("Invalid cash history cursor."); }
            var cursor = JsonSerializer.Deserialize<CashHistoryCursor>(bytes) ?? throw new ArgumentException("Invalid cash history cursor.");
            ValidateCashHistoryDate(cursor.BusinessDate);
            if (cursor.Version != 2 || cursor.BranchId != branch || cursor.SchemaCookie != schemaCookie || cursor.RequestedBusinessDate != query.BusinessDate ||
                cursor.ReceiptNumber != query.ReceiptNumber || cursor.TableSessionId != query.TableSessionId || cursor.PaymentMethod != query.PaymentMethod ||
                cursor.TableId != query.TableId || cursor.OrderBatchId != query.OrderBatchId ||
                cursor.Status != query.Status || cursor.Limit != query.Limit || cursor.Before <= 0 || cursor.HighWater < cursor.Before ||
                (query.BusinessDate is not null && cursor.BusinessDate != query.BusinessDate))
            { throw new ArgumentException("Cash history cursor does not match the query."); }
            return cursor;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
        { throw new ArgumentException("Invalid cash history cursor.", error); }
    }
}
