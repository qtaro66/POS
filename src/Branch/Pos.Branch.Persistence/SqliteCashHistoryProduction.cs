using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<CashHistoryProductionPage?> ReadCashHistoryProductionOrdersAsync(string branchId, string requestId,
        DateTimeOffset now, CashHistoryProductionQuery query, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) => ((SqliteTableSessionCommandTransaction)transaction)
            .ReadCashHistoryProductionOrdersAsync(branchId, requestId, now, query, cancellation), token);
    public Task<CashHistoryProductionDetail?> ReadCashHistoryProductionOrderDetailAsync(string branchId, string requestId,
        string snapshotId, DateTimeOffset now, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) => ((SqliteTableSessionCommandTransaction)transaction)
            .ReadCashHistoryProductionOrderDetailAsync(branchId, requestId, snapshotId, now, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private const string ProductionColumns = "rowid,snapshot_id,request_id,order_batch_id,table_session_id,table_id,production_station_id,content_schema_version,content_json,content_sha256,created_at";
    private sealed record ProductionCursor(int Version, string BranchId, string CashRequestId, string TableSessionId,
        string? OrderBatchId, string? OrderLineId, string? ProductionStationId, int Limit, long SchemaCookie, long HighWater, long Before);

    private async Task<string?> ProductionCashSubject(string branchId, string requestId, DateTimeOffset now, CancellationToken token)
    {
        ValidateReportId(branchId); ValidateReportId(requestId); RequireClosingReportUtc(now);
        using var read = CreateCommand($"SELECT {CashHistoryColumns} FROM {CashHistoryJoin} WHERE sale.branch_id=$branch AND sale.request_id=$request;");
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$request", requestId);
        await using var reader = await read.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadCashHistoryItem(reader).TableSessionId : null;
    }

    internal async Task<CashHistoryProductionPage?> ReadCashHistoryProductionOrdersAsync(string branchId, string requestId,
        DateTimeOffset now, CashHistoryProductionQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var id in new[] { query.OrderBatchId, query.OrderLineId, query.ProductionStationId })
        { if (id is not null) { ValidateReportId(id); } }
        if (query.Limit is < 1 or > 50) { throw new ArgumentException("Invalid production query."); }
        var session = await ProductionCashSubject(branchId, requestId, now, token);
        if (session is null) { return null; }
        using var schema = CreateCommand("PRAGMA schema_version;");
        var cookie = Convert.ToInt64(await schema.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        var cursor = query.Cursor is null ? null : DecodeProductionCursor(query.Cursor, branchId, requestId, session, query, cookie);
        using var maximum = CreateCommand("SELECT COALESCE(MAX(rowid),0) FROM production_order_snapshots WHERE branch_id=$branch AND table_session_id=$session;");
        maximum.Parameters.AddWithValue("$branch", branchId); maximum.Parameters.AddWithValue("$session", session);
        var high = cursor?.HighWater ?? Convert.ToInt64(await maximum.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        using var read = CreateCommand($"""
            SELECT {ProductionColumns} FROM production_order_snapshots
            WHERE branch_id=$branch AND table_session_id=$session AND rowid<=$high AND ($before IS NULL OR rowid<$before)
                AND ($batch IS NULL OR order_batch_id=$batch) AND ($station IS NULL OR production_station_id=$station)
            ORDER BY rowid DESC;
            """);
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$session", session);
        read.Parameters.AddWithValue("$high", high); read.Parameters.AddWithValue("$before", (object?)cursor?.Before ?? DBNull.Value);
        read.Parameters.AddWithValue("$batch", (object?)query.OrderBatchId ?? DBNull.Value);
        read.Parameters.AddWithValue("$station", (object?)query.ProductionStationId ?? DBNull.Value);
        var rows = new List<(long Row, CashHistoryProductionItem Item)>();
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var captured = ReadProductionSnapshot(reader);
                if (query.OrderLineId is not null && !captured.Item.OrderLineIds.Contains(query.OrderLineId, StringComparer.Ordinal)) { continue; }
                rows.Add((reader.GetInt64(0), captured.Item));
                if (rows.Count > query.Limit) { break; }
            }
        }
        string? next = null;
        if (rows.Count > query.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new ProductionCursor(1, branchId, requestId,
                session, query.OrderBatchId, query.OrderLineId, query.ProductionStationId, query.Limit, cookie, high, rows[^1].Row)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        return new(1, branchId, now, requestId, session, rows.Select(row => row.Item).ToArray(), next);
    }

    internal async Task<CashHistoryProductionDetail?> ReadCashHistoryProductionOrderDetailAsync(string branchId, string requestId,
        string snapshotId, DateTimeOffset now, CancellationToken token)
    {
        ProductionIdentifier(snapshotId, 1024);
        var session = await ProductionCashSubject(branchId, requestId, now, token);
        if (session is null) { return null; }
        using var read = CreateCommand($"SELECT {ProductionColumns} FROM production_order_snapshots WHERE branch_id=$branch AND table_session_id=$session AND snapshot_id=$snapshot;");
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$session", session); read.Parameters.AddWithValue("$snapshot", snapshotId);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { throw new KeyNotFoundException("Production snapshot not found."); }
        var captured = ReadProductionSnapshot(reader);
        return new(1, branchId, now, requestId, session, captured.Item, captured.Lines);
    }

    private static (CashHistoryProductionItem Item, IReadOnlyList<CashHistoryProductionLine> Lines) ReadProductionSnapshot(SqliteDataReader reader)
    {
        try
        {
            var snapshot = reader.GetString(1); ProductionIdentifier(snapshot, 1024);
            foreach (var index in new[] { 2, 3, 4, 5, 6 }) { ValidateReportId(reader.GetString(index)); }
            var json = reader.GetString(8);
            if (reader.GetInt64(7) != 1 || Encoding.UTF8.GetByteCount(json) > 1024 * 1024 ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), reader.GetString(9), StringComparison.OrdinalIgnoreCase))
            { throw InvalidProduction(); }
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement; CashEvidenceObject(root);
            var submitted = ProductionTimestamp(reader.GetString(10));
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("orderBatchId").GetString() != reader.GetString(3) ||
                root.GetProperty("tableSessionId").GetString() != reader.GetString(4) || root.GetProperty("tableId").GetString() != reader.GetString(5) ||
                root.GetProperty("productionStationId").GetString() != reader.GetString(6) ||
                ProductionTimestamp(root.GetProperty("submittedAt").GetString()!) != submitted)
            { throw InvalidProduction(); }
            var rawLines = root.GetProperty("lines");
            if (rawLines.ValueKind != JsonValueKind.Array || rawLines.GetArrayLength() == 0) { throw InvalidProduction(); }
            var lines = new List<CashHistoryProductionLine>(); var ids = new HashSet<string>(StringComparer.Ordinal); var previous = 0;
            foreach (var raw in rawLines.EnumerateArray())
            {
                CashEvidenceObject(raw);
                var id = raw.GetProperty("orderLineId").GetString()!; ValidateReportId(id);
                var sequence = raw.GetProperty("lineSequence").GetInt32(); var quantity = raw.GetProperty("quantity").GetInt32();
                if (!ids.Add(id) || sequence <= previous || quantity < 1) { throw InvalidProduction(); }
                previous = sequence;
                var fulfillment = raw.GetProperty("fulfillmentType").GetString() switch
                { "dineIn" => "DineIn", "takeaway" => "Takeaway", _ => throw InvalidProduction() };
                var options = new List<CashHistoryProductionOption>();
                foreach (var option in raw.GetProperty("options").EnumerateArray())
                {
                    CashEvidenceObject(option);
                    var group = option.GetProperty("optionGroupId").GetString(); if (group is not null) { ValidateReportId(group); }
                    var count = option.GetProperty("quantityPerItem").GetInt32(); if (count < 1) { throw InvalidProduction(); }
                    options.Add(new(group, ProductionText(option.GetProperty("displayName"), 512, false)!, count));
                }
                lines.Add(new(id, sequence, ProductionText(raw.GetProperty("displayName"), 512, false)!, quantity, fulfillment,
                    ProductionText(raw.GetProperty("comment"), 4096, true), options.ToArray()));
            }
            return (new(snapshot, reader.GetString(3), reader.GetString(2), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                submitted, lines.Count, lines.Select(line => line.OrderLineId).ToArray()), lines.ToArray());
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or OverflowException or FormatException)
        { throw new InvalidDataException("Saved production snapshot is unavailable.", error); }
    }

    private static void ProductionIdentifier(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim() || value.Any(char.IsControl))
        { throw new ArgumentException("Invalid production identifier."); }
    }
    private static string? ProductionText(JsonElement raw, int maximum, bool nullable)
    {
        if (nullable && raw.ValueKind == JsonValueKind.Null) { return null; }
        var value = raw.GetString();
        if (value is null || (!nullable && string.IsNullOrWhiteSpace(value)) || value.Length > maximum ||
            value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) { throw InvalidProduction(); }
        return value;
    }
    private static InvalidDataException InvalidProduction() => new("Saved production snapshot is unavailable.");

    private static DateTimeOffset ProductionTimestamp(string value)
    {
        if (value is null || value.Length < 20 || !value.Contains('T', StringComparison.Ordinal) ||
            !(value.EndsWith('Z') || (value.Length >= 6 && value[^3] == ':' && value[^6] is '+' or '-')) ||
            !DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) { throw InvalidProduction(); }
        return parsed.ToUniversalTime();
    }

    private static ProductionCursor DecodeProductionCursor(string value, string branchId, string requestId, string session,
        CashHistoryProductionQuery query, long cookie)
    {
        try
        {
            if (value.Length is < 1 or > 4096 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            { throw new ArgumentException("Invalid production cursor."); }
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
            using var document = JsonDocument.Parse(bytes);
            var names = new HashSet<string>(["Version", "BranchId", "CashRequestId", "TableSessionId", "OrderBatchId", "OrderLineId", "ProductionStationId", "Limit", "SchemaCookie", "HighWater", "Before"], StringComparer.Ordinal);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != names.Count ||
                root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != names.Count || root.EnumerateObject().Any(p => !names.Contains(p.Name)))
            { throw new ArgumentException("Invalid production cursor."); }
            var cursor = JsonSerializer.Deserialize<ProductionCursor>(bytes) ?? throw new ArgumentException("Invalid production cursor.");
            if (cursor.Version != 1 || cursor.BranchId != branchId || cursor.CashRequestId != requestId || cursor.TableSessionId != session ||
                cursor.OrderBatchId != query.OrderBatchId || cursor.OrderLineId != query.OrderLineId || cursor.ProductionStationId != query.ProductionStationId ||
                cursor.Limit != query.Limit || cursor.SchemaCookie != cookie || cursor.Before < 1 || cursor.HighWater < cursor.Before)
            { throw new ArgumentException("Production cursor does not match the query."); }
            return cursor;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { throw new ArgumentException("Invalid production cursor.", error); }
    }
}
