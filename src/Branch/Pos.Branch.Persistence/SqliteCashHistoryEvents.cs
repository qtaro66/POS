using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<CashHistoryEventPage?> ReadCashHistoryEventsAsync(string branchId, string requestId,
        DateTimeOffset now, CashHistoryEventQuery query, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadCashHistoryEventsAsync(branchId, requestId, now, query, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private sealed record CashEventCursor(int Version, string BranchId, string CashRequestId, string TableSessionId,
        string Source, int Limit, long SchemaCookie, long HighWater, long Before);
    private const int MaximumCancellationEvidenceBytes = 1024 * 1024;

    internal async Task<CashHistoryEventPage?> ReadCashHistoryEventsAsync(string branchId, string requestId,
        DateTimeOffset now, CashHistoryEventQuery query, CancellationToken token)
    {
        ValidateReportId(branchId); ValidateReportId(requestId); RequireClosingReportUtc(now);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Source is not ("Session" or "Cancellation") || query.Limit is < 1 or > 50)
        { throw new ArgumentException("Invalid cash history event query."); }
        string sessionId;
        using (var subject = CreateCommand($"SELECT {CashHistoryColumns} FROM {CashHistoryJoin} WHERE sale.branch_id=$branch AND sale.request_id=$request;"))
        {
            subject.Parameters.AddWithValue("$branch", branchId); subject.Parameters.AddWithValue("$request", requestId);
            await using var reader = await subject.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) { return null; }
            sessionId = ReadCashHistoryItem(reader).TableSessionId;
        }
        using var schema = CreateCommand("PRAGMA schema_version;");
        var cookie = Convert.ToInt64(await schema.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        var cursor = query.Cursor is null ? null : DecodeCashEventCursor(query.Cursor, branchId, requestId, sessionId, query, cookie);
        var session = query.Source == "Session";
        if (!session)
        {
            using var integrity = CreateCommand("SELECT EXISTS(SELECT 1 FROM cancellation_confirmations evidence LEFT JOIN cancellation_requests request USING(branch_id,cancellation_request_id) WHERE evidence.branch_id=$branch AND request.cancellation_request_id IS NULL);");
            integrity.Parameters.AddWithValue("$branch", branchId);
            if (Convert.ToInt64(await integrity.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0) { throw InvalidCashEvent(); }
        }
        var source = session ? "table_session_audit evidence" : "cancellation_confirmations evidence JOIN cancellation_requests request USING(branch_id,cancellation_request_id)";
        var scope = session ? "evidence.branch_id=$branch AND evidence.table_session_id=$session" : "evidence.branch_id=$branch AND request.table_session_id=$session";
        long high;
        if (cursor is not null) { high = cursor.HighWater; }
        else
        {
            using var maximum = CreateCommand($"SELECT COALESCE(MAX(evidence.rowid),0) FROM {source} WHERE {scope};");
            maximum.Parameters.AddWithValue("$branch", branchId); maximum.Parameters.AddWithValue("$session", sessionId);
            high = Convert.ToInt64(await maximum.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        }
        var columns = session
            ? "evidence.rowid,evidence.audit_id,evidence.request_id,evidence.occurred_at,evidence.employee_id,evidence.device_id,evidence.action,evidence.previous_status,evidence.current_status,evidence.previous_table_id,evidence.current_table_id,evidence.table_version,evidence.reason"
            : "evidence.rowid,evidence.cancellation_request_id,evidence.occurred_at,evidence.employee_id,evidence.details_json";
        using var read = CreateCommand($"SELECT {columns} FROM {source} WHERE {scope} AND evidence.rowid<=$high AND ($before IS NULL OR evidence.rowid<$before) ORDER BY evidence.rowid DESC LIMIT $limit;");
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$session", sessionId);
        read.Parameters.AddWithValue("$high", high); read.Parameters.AddWithValue("$before", (object?)cursor?.Before ?? DBNull.Value);
        read.Parameters.AddWithValue("$limit", query.Limit + 1);
        var rows = new List<(long Row, CashHistoryEvent Event)>();
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                rows.Add((reader.GetInt64(0), session ? ReadCashSessionEvent(reader) : ReadCashCancellationEvent(reader)));
            }
        }
        string? next = null;
        if (rows.Count > query.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new CashEventCursor(1, branchId, requestId,
                sessionId, query.Source, query.Limit, cookie, high, rows[^1].Row))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        return new(1, branchId, now, requestId, sessionId, query.Source, rows.Select(row => row.Event).ToArray(), next);
    }

    private static CashHistoryEvent ReadCashSessionEvent(SqliteDataReader reader)
    {
        try
        {
            var auditId = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(auditId) || auditId != auditId.Trim() || auditId.Length > 1016 || auditId.Any(char.IsControl))
            { throw InvalidCashEvent(); }
            foreach (var index in new[] { 2, 4, 5, 9, 10 }) { ValidateReportId(reader.GetString(index)); }
            var action = ReadCashEventEnum<TableSessionAction>(reader.GetString(6));
            var previous = reader.IsDBNull(7) ? null : ReadCashEventEnum<TableSessionStatus>(reader.GetString(7));
            var current = ReadCashEventEnum<TableSessionStatus>(reader.GetString(8));
            var version = reader.GetInt64(11);
            if (version < 1) { throw InvalidCashEvent(); }
            var reason = reader.IsDBNull(12) ? null : reader.GetString(12);
            if (reason is not null && (reason.Length > 4096 || reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))))
            { throw InvalidCashEvent(); }
            return new("session:" + reader.GetString(1), "Session", reader.GetString(2), ReadReportTimestamp(reader.GetString(3)),
                reader.GetString(4), reader.GetString(5), new(action, previous, current, reader.GetString(9), reader.GetString(10), version, reason), null);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or FormatException)
        { throw new InvalidDataException("Saved session evidence is unavailable.", error); }
    }

    private static CashHistoryEvent ReadCashCancellationEvent(SqliteDataReader reader)
    {
        try
        {
            ValidateReportId(reader.GetString(1)); ValidateReportId(reader.GetString(3));
            var json = reader.GetString(4);
            if (Encoding.UTF8.GetByteCount(json) > MaximumCancellationEvidenceBytes) { throw InvalidCashEvent(); }
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0) { throw InvalidCashEvent(); }
            var lines = new List<CashHistoryCancellationLine>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            long totalCancelled = 0;
            foreach (var raw in document.RootElement.EnumerateArray())
            {
                CashEvidenceObject(raw);
                var (checkedId, checkedCounts) = ReadCashProductionCounts(raw.GetProperty("Checked"));
                var (cancelledId, cancelledCounts) = ReadCashProductionCounts(raw.GetProperty("Cancelled"));
                var requested = raw.GetProperty("RequestedQuantity").GetInt32();
                var notCancelled = raw.GetProperty("NotCancelledQuantity").GetInt64();
                var cancelled = CashEvidenceQuantity(cancelledCounts);
                if (checkedId != cancelledId || !ids.Add(checkedId) || requested < 1 ||
                    CashEvidenceQuantity(checkedCounts) != requested || cancelledCounts.NotStarted > checkedCounts.NotStarted ||
                    cancelledCounts.InProgress > checkedCounts.InProgress || cancelledCounts.Completed > checkedCounts.Completed ||
                    cancelledCounts.NotApplicable > checkedCounts.NotApplicable || notCancelled != requested - cancelled || notCancelled < 0)
                { throw InvalidCashEvent(); }
                totalCancelled = checked(totalCancelled + cancelled);
                lines.Add(new(checkedId, checkedCounts, cancelledCounts, requested, notCancelled));
            }
            if (totalCancelled == 0) { throw InvalidCashEvent(); }
            var outcome = lines.All(line => line.NotCancelledQuantity == 0) ? "Confirmed" : "PartiallyConfirmed";
            return new("cancellation:" + reader.GetString(1), "Cancellation", reader.GetString(1), ReadReportTimestamp(reader.GetString(2)),
                reader.GetString(3), null, null, new(outcome, lines.ToArray()));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or OverflowException or FormatException)
        { throw new InvalidDataException("Saved cancellation evidence is unavailable.", error); }
    }

    private static (string Id, CashHistoryProductionCounts Counts) ReadCashProductionCounts(JsonElement value)
    {
        CashEvidenceObject(value);
        var id = value.GetProperty("OrderLineId").GetString()!; ValidateReportId(id);
        var counts = new CashHistoryProductionCounts(value.GetProperty("NotStarted").GetInt32(), value.GetProperty("InProgress").GetInt32(),
            value.GetProperty("Completed").GetInt32(), value.GetProperty("NotApplicable").GetInt32());
        if (counts.NotStarted < 0 || counts.InProgress < 0 || counts.Completed < 0 || counts.NotApplicable < 0) { throw InvalidCashEvent(); }
        return (id, counts);
    }
    private static long CashEvidenceQuantity(CashHistoryProductionCounts counts) =>
        (long)counts.NotStarted + counts.InProgress + counts.Completed + counts.NotApplicable;
    private static void CashEvidenceObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
        { throw InvalidCashEvent(); }
    }
    private static string ReadCashEventEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(parsed) && parsed.ToString() == value ? value : throw InvalidCashEvent();
    private static InvalidDataException InvalidCashEvent() => new("Saved cash history evidence is unavailable.");

    private static CashEventCursor DecodeCashEventCursor(string value, string branchId, string requestId, string sessionId,
        CashHistoryEventQuery query, long schemaCookie)
    {
        try
        {
            if (value.Length is < 1 or > 4096 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            { throw new ArgumentException("Invalid cash history event cursor."); }
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
            using var document = JsonDocument.Parse(bytes);
            var names = new HashSet<string>(["Version", "BranchId", "CashRequestId", "TableSessionId", "Source", "Limit", "SchemaCookie", "HighWater", "Before"], StringComparer.Ordinal);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != names.Count ||
                document.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != names.Count ||
                document.RootElement.EnumerateObject().Any(p => !names.Contains(p.Name)))
            { throw new ArgumentException("Invalid cash history event cursor."); }
            var cursor = JsonSerializer.Deserialize<CashEventCursor>(bytes) ?? throw new ArgumentException("Invalid cash history event cursor.");
            if (cursor.Version != 1 || cursor.BranchId != branchId || cursor.CashRequestId != requestId || cursor.TableSessionId != sessionId ||
                cursor.Source != query.Source || cursor.Limit != query.Limit || cursor.SchemaCookie != schemaCookie ||
                cursor.Before < 1 || cursor.HighWater < cursor.Before)
            { throw new ArgumentException("Cash history event cursor does not match the query."); }
            return cursor;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { throw new ArgumentException("Invalid cash history event cursor.", error); }
    }
}
