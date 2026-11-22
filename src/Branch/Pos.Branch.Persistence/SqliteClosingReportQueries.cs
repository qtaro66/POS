using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<ClosingReportPage> ReadClosingReportPageAsync(string branchId, DateTimeOffset now,
        ClosingReportQuery query, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadClosingReportPageAsync(branchId, now, query, cancellation), token);

    public Task<ClosingReportDetail?> ReadClosingReportDetailAsync(string branchId, string requestId,
        DateTimeOffset now, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadClosingReportDetailAsync(branchId, requestId, now, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private sealed record ReportCursor(int Version, string BranchId, string Status, string? StoreSessionId,
        string? ShiftId, string? BusinessDate, int Limit, long HighWater, long Before, string LifecycleStatus);
    private const string ReportColumns = """
        sequence,request_id,report_id,json_extract(payload_json,'$.storeSessionId'),
        json_extract(payload_json,'$.shiftId'),json_extract(payload_json,'$.businessDate'),operation,
        created_at,attempt_count,available_at,lease_expires_at,delivered_at,last_failure,payload_json
        """;
    private static readonly JsonSerializerOptions ReportSnapshotOptions = new() { PropertyNameCaseInsensitive = true };

    internal async Task<ClosingReportPage> ReadClosingReportPageAsync(string branchId, DateTimeOffset now,
        ClosingReportQuery query, CancellationToken token)
    {
        ValidateReportId(branchId);
        RequireClosingReportUtc(now);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Status is not ("All" or "Pending" or "Delivered") || query.LifecycleStatus is not ("All" or "Pending" or "Delivered" or "NotRecorded") ||
            query.Limit is < 1 or > 50) { throw new ArgumentException("Invalid closing report query."); }
        if (query.StoreSessionId is not null) { ValidateReportId(query.StoreSessionId); }
        if (query.ShiftId is not null) { ValidateReportId(query.ShiftId); }
        if (query.BusinessDate is not null && !DateOnly.TryParseExact(query.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException("Invalid closing report business date.");
        }
        ReportCursor? cursor = null;
        if (query.Cursor is not null) { cursor = DecodeReportCursor(query.Cursor, branchId, query); }
        await RequireConsistentLifecycleReportJobsAsync(branchId, token);
        long highWater;
        if (cursor is not null) { highWater = cursor.HighWater; }
        else
        {
            using var maximum = CreateCommand("SELECT COALESCE(MAX(sequence),0) FROM closing_report_outbox WHERE branch_id=$branch;");
            maximum.Parameters.AddWithValue("$branch", branchId);
            highWater = Convert.ToInt64(await maximum.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        }
        using var read = CreateCommand($"""
            SELECT {ReportColumns} FROM closing_report_outbox
            WHERE branch_id=$branch AND sequence <= $high AND ($before IS NULL OR sequence < $before)
                AND ($status='All' OR ($status='Pending' AND delivered_at IS NULL) OR ($status='Delivered' AND delivered_at IS NOT NULL))
                AND ($lifecycle='All'
                    OR ($lifecycle='NotRecorded' AND NOT EXISTS (
                        SELECT 1 FROM closing_report_lifecycle_snapshots frozen
                        WHERE frozen.branch_id=closing_report_outbox.branch_id AND frozen.request_id=closing_report_outbox.request_id))
                    OR EXISTS (SELECT 1 FROM closing_report_context_outbox context
                        WHERE context.branch_id=closing_report_outbox.branch_id AND context.request_id=closing_report_outbox.request_id
                            AND (($lifecycle='Pending' AND context.delivered_at IS NULL)
                                OR ($lifecycle='Delivered' AND context.delivered_at IS NOT NULL))))
                AND ($store IS NULL OR json_extract(payload_json,'$.storeSessionId')=$store)
                AND ($shift IS NULL OR json_extract(payload_json,'$.shiftId')=$shift)
                AND ($day IS NULL OR json_extract(payload_json,'$.businessDate')=$day)
            ORDER BY sequence DESC LIMIT $limit;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$high", highWater);
        read.Parameters.AddWithValue("$before", (object?)cursor?.Before ?? DBNull.Value);
        read.Parameters.AddWithValue("$status", query.Status);
        read.Parameters.AddWithValue("$lifecycle", query.LifecycleStatus);
        read.Parameters.AddWithValue("$store", (object?)query.StoreSessionId ?? DBNull.Value);
        read.Parameters.AddWithValue("$shift", (object?)query.ShiftId ?? DBNull.Value);
        read.Parameters.AddWithValue("$day", (object?)query.BusinessDate ?? DBNull.Value);
        read.Parameters.AddWithValue("$limit", query.Limit + 1);
        var rows = new List<(long Sequence, ClosingReportListItem Item)>();
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token)) { rows.Add((reader.GetInt64(0), ReadReportItem(reader, now))); }
        }
        string? next = null;
        if (rows.Count > query.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = EncodeReportCursor(new(2, branchId, query.Status, query.StoreSessionId, query.ShiftId,
                query.BusinessDate, query.Limit, highWater, rows[^1].Sequence, query.LifecycleStatus));
        }
        return new(branchId, now, rows.Select(row => row.Item).ToArray(), next);
    }

    private async Task RequireConsistentLifecycleReportJobsAsync(string branchId, CancellationToken token)
    {
        using var integrity = CreateCommand("""
            SELECT EXISTS (
                SELECT 1 FROM closing_report_lifecycle_snapshots frozen
                LEFT JOIN closing_report_context_outbox context ON context.branch_id=frozen.branch_id AND context.request_id=frozen.request_id
                WHERE frozen.branch_id=$branch AND context.context_id IS NULL)
            OR EXISTS (
                SELECT 1 FROM closing_report_context_outbox context
                LEFT JOIN closing_report_lifecycle_snapshots frozen ON frozen.branch_id=context.branch_id AND frozen.request_id=context.request_id
                LEFT JOIN closing_report_outbox parent ON parent.branch_id=context.branch_id AND parent.request_id=context.request_id
                WHERE context.branch_id=$branch AND (frozen.request_id IS NULL OR parent.report_id IS NULL
                    OR context.context_id != 'lifecycle:' || parent.report_id
                    OR (context.delivered_at IS NOT NULL AND parent.delivered_at IS NULL)));
            """);
        integrity.Parameters.AddWithValue("$branch", branchId);
        if (Convert.ToInt64(await integrity.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidDataException("Saved lifecycle delivery is unavailable.");
        }
        using var metadata = CreateCommand("""
            SELECT context.created_at,context.available_at,context.lease_expires_at,context.delivered_at,
                context.attempt_count,context.lease_id,context.last_failure,parent.delivered_at
            FROM closing_report_context_outbox context JOIN closing_report_outbox parent
                ON parent.branch_id=context.branch_id AND parent.request_id=context.request_id
            WHERE context.branch_id=$branch;
            """);
        metadata.Parameters.AddWithValue("$branch", branchId);
        await using var reader = await metadata.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            _ = ReadReportTimestamp(reader.GetString(0));
            _ = ReadReportTimestamp(reader.GetString(1));
            if (!reader.IsDBNull(2)) { _ = ReadReportTimestamp(reader.GetString(2)); }
            if (!reader.IsDBNull(3))
            {
                _ = ReadReportTimestamp(reader.GetString(3));
                _ = ReadReportTimestamp(reader.GetString(7));
                if (reader.GetInt64(4) == 0 || !reader.IsDBNull(5) || !reader.IsDBNull(6))
                {
                    throw new InvalidDataException("Saved lifecycle confirmation is inconsistent.");
                }
            }
            if (reader.GetInt64(4) is < 0 or > int.MaxValue)
            {
                throw new InvalidDataException("Saved lifecycle attempt count is unavailable.");
            }
        }
    }

    internal async Task<ClosingReportDetail?> ReadClosingReportDetailAsync(string branchId, string requestId,
        DateTimeOffset now, CancellationToken token)
    {
        ValidateReportId(branchId);
        ValidateReportId(requestId);
        RequireClosingReportUtc(now);
        using var read = CreateCommand($"SELECT {ReportColumns} FROM closing_report_outbox WHERE branch_id=$branch AND request_id=$request;");
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$request", requestId);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { return null; }
        var item = ReadReportItem(reader, now);
        var snapshot = JsonSerializer.Deserialize<ClosingReportSnapshot>(reader.GetString(13), ReportSnapshotOptions)
            ?? throw new InvalidDataException("Saved closing report is unavailable.");
        if (snapshot.SchemaVersion != 1 || snapshot.BranchId != branchId || snapshot.RequestId != requestId ||
            snapshot.ReportId != item.ReportId || snapshot.StoreSessionId != item.StoreSessionId ||
            snapshot.ShiftId != item.ShiftId || snapshot.Operation != item.Operation || snapshot.Closing is null ||
            snapshot.Closing.Context is null || snapshot.Closing.StoreSessionId != item.StoreSessionId ||
            snapshot.Closing.ShiftId != item.ShiftId || snapshot.Closing.ClosesStore != (item.Operation == "CloseStore") ||
            snapshot.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != item.BusinessDate)
        {
            throw new InvalidDataException("Saved closing report identity is inconsistent.");
        }
        await reader.DisposeAsync();
        var lifecycle = await ReadSavedClosingReportLifecycleAsync(branchId, requestId, snapshot, token);
        var lifecycleDelivery = await ReadClosingLifecycleDeliveryAsync(branchId, requestId, now, lifecycle is not null, token);
        return new(branchId, item, snapshot, lifecycle, lifecycleDelivery);
    }

    private static ClosingReportListItem ReadReportItem(SqliteDataReader reader, DateTimeOffset now)
    {
        var available = ReadReportTimestamp(reader.GetString(9));
        DateTimeOffset? lease = reader.IsDBNull(10) ? null : ReadReportTimestamp(reader.GetString(10));
        DateTimeOffset? delivered = reader.IsDBNull(11) ? null : ReadReportTimestamp(reader.GetString(11));
        var hasFailure = !reader.IsDBNull(12);
        var failure = hasFailure ? reader.GetString(12) : null;
        if (!Enum.TryParse<ClosingReportDeliveryFailure>(failure, out var known) || !Enum.IsDefined(known) || known.ToString() != failure) { failure = null; }
        var state = delivered is not null ? "Delivered" : lease > now ? "InFlight" : lease is not null ? "LeaseExpired"
            : hasFailure && available > now ? "RetryScheduled" : "Queued";
        return new(reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6), ReadReportTimestamp(reader.GetString(7)), state, reader.GetInt32(8), available, lease, delivered, failure);
    }

    private static DateTimeOffset ReadReportTimestamp(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime() : throw new InvalidDataException("Saved closing report timestamp is unavailable.");
    private static void ValidateReportId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value != value.Trim() || value.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid closing report identifier.");
        }
    }
    private static string EncodeReportCursor(ReportCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static ReportCursor DecodeReportCursor(string value, string branch, ClosingReportQuery query)
    {
        try
        {
            if (value.Length is < 1 or > 4096 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))) { throw new ArgumentException("Invalid closing report cursor."); }
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 10 ||
                document.RootElement.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 10)
            {
                throw new ArgumentException("Invalid closing report cursor.");
            }
            var expectedNames = new HashSet<string>(["Version", "BranchId", "Status", "StoreSessionId", "ShiftId", "BusinessDate", "Limit", "HighWater", "Before", "LifecycleStatus"], StringComparer.Ordinal);
            if (!document.RootElement.EnumerateObject().All(property => expectedNames.Contains(property.Name))) { throw new ArgumentException("Invalid closing report cursor."); }
            var cursor = JsonSerializer.Deserialize<ReportCursor>(bytes) ?? throw new ArgumentException("Invalid closing report cursor.");
            if (cursor.Version != 2 || cursor.BranchId != branch || cursor.Status != query.Status || cursor.LifecycleStatus != query.LifecycleStatus || cursor.StoreSessionId != query.StoreSessionId ||
                cursor.ShiftId != query.ShiftId || cursor.BusinessDate != query.BusinessDate || cursor.Limit != query.Limit ||
                cursor.Before <= 0 || cursor.HighWater < cursor.Before) { throw new ArgumentException("Closing report cursor does not match the query."); }
            return cursor;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException("Invalid closing report cursor.", error);
        }
    }
}
