using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<CashHistoryPrintEvidence?> ReadCashHistoryPrintEvidenceAsync(string branchId, string requestId,
        string snapshotId, DateTimeOffset now, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) => ((SqliteTableSessionCommandTransaction)transaction)
            .ReadCashHistoryPrintEvidenceAsync(branchId, requestId, snapshotId, now, cancellation), token);
    public Task<CashHistoryPrintAttemptsPage?> ReadCashHistoryPrintAttemptsAsync(string branchId, string requestId,
        string snapshotId, string printJobId, DateTimeOffset now, CashHistoryPrintQuery query, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) => ((SqliteTableSessionCommandTransaction)transaction)
            .ReadCashHistoryPrintAttemptsAsync(branchId, requestId, snapshotId, printJobId, now, query, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private const string HistoryPrintJobColumns = "rowid,print_job_id,request_id,snapshot_id,target_station,status,attempt_count,created_at,is_reprint,original_print_job_id";
    private const string HistoryPrintJobScope = "branch_id=$branch AND table_session_id=$session AND snapshot_id=$snapshot AND purpose='KitchenOrder'";
    private sealed record HistoryPrintCursor(int Version, string ScopeHash, int Limit, long SchemaCookie, long HighWater, long Before);

    internal async Task<CashHistoryPrintEvidence?> ReadCashHistoryPrintEvidenceAsync(string branchId, string requestId,
        string snapshotId, DateTimeOffset now, CancellationToken token)
    {
        var production = await ReadCashHistoryProductionOrderDetailAsync(branchId, requestId, snapshotId, now, token);
        if (production is null) { return null; }
        using var read = CreateCommand($"SELECT {HistoryPrintJobColumns} FROM print_jobs WHERE {HistoryPrintJobScope} ORDER BY rowid DESC LIMIT 2;");
        BindHistoryPrintSubject(read, branchId, production.TableSessionId, snapshotId);
        CashHistoryPrintJob? job = null;
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token)) { job = ReadHistoryPrintJob(reader, production.Snapshot); }
            if (await reader.ReadAsync(token)) { throw InvalidHistoryPrint(); }
        }
        return new(1, branchId, now, requestId, production.TableSessionId, snapshotId, job);
    }

    internal async Task<CashHistoryPrintAttemptsPage?> ReadCashHistoryPrintAttemptsAsync(string branchId, string requestId,
        string snapshotId, string printJobId, DateTimeOffset now, CashHistoryPrintQuery query, CancellationToken token)
    {
        ValidateHistoryPrintQuery(query); ProductionIdentifier(printJobId, 1024);
        var production = await ReadCashHistoryProductionOrderDetailAsync(branchId, requestId, snapshotId, now, token);
        if (production is null) { return null; }
        var job = await FindHistoryPrintJob(branchId, production, printJobId, token)
            ?? throw new CashHistoryPrintJobNotFoundException();
        var cookie = await HistoryPrintSchemaCookie(token);
        var cursor = query.Cursor is null ? null : DecodeHistoryPrintCursor(query.Cursor, branchId, requestId,
            production.TableSessionId, snapshotId, "Attempts", printJobId, query.Limit, cookie);
        using var maximum = CreateCommand("SELECT COALESCE(MAX(rowid),0) FROM print_attempts WHERE print_job_id=$job;");
        maximum.Parameters.AddWithValue("$job", printJobId);
        var high = cursor?.HighWater ?? Convert.ToInt64(await maximum.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        using var read = CreateCommand("""
            SELECT rowid,print_attempt_id,attempt_number,status,automatic_retry_allowed,started_at,first_byte_at,finished_at
            FROM print_attempts WHERE print_job_id=$job AND rowid<=$high AND ($before IS NULL OR rowid<$before)
            ORDER BY rowid DESC LIMIT $limit;
            """);
        read.Parameters.AddWithValue("$job", printJobId); BindHistoryPrintPage(read, query.Limit, high, cursor?.Before);
        var rows = new List<(long Row, CashHistoryPrintAttempt Item)>();
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token)) { rows.Add((reader.GetInt64(0), ReadHistoryPrintAttempt(reader, job.AttemptCount))); }
        }
        string? next = null;
        if (rows.Count > query.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = EncodeHistoryPrintCursor(new(1, HistoryPrintScopeHash(branchId, requestId, production.TableSessionId,
                snapshotId, "Attempts", printJobId), query.Limit, cookie, high, rows[^1].Row));
        }
        return new(1, branchId, now, requestId, production.TableSessionId, snapshotId, job,
            rows.Select(row => row.Item).ToArray(), next);
    }

    private async Task<CashHistoryPrintJob?> FindHistoryPrintJob(string branchId, CashHistoryProductionDetail production,
        string printJobId, CancellationToken token)
    {
        using var read = CreateCommand($"SELECT {HistoryPrintJobColumns} FROM print_jobs WHERE {HistoryPrintJobScope} AND print_job_id=$job;");
        BindHistoryPrintSubject(read, branchId, production.TableSessionId, production.Snapshot.SnapshotId);
        read.Parameters.AddWithValue("$job", printJobId);
        await using var reader = await read.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadHistoryPrintJob(reader, production.Snapshot) : null;
    }

    private static CashHistoryPrintJob ReadHistoryPrintJob(SqliteDataReader reader, CashHistoryProductionItem snapshot)
    {
        try
        {
            foreach (var index in new[] { 1, 2, 3, 4, 5, 7 }) { if (reader.GetValue(index) is not string) { throw InvalidHistoryPrint(); } }
            if (reader.GetValue(6) is not long count || count is < 0 or > int.MaxValue ||
                reader.GetValue(8) is not long reprint || reprint is not (0 or 1)) { throw InvalidHistoryPrint(); }
            var id = reader.GetString(1); ProductionIdentifier(id, 1024);
            var request = reader.GetString(2); ValidateReportId(request);
            var snapshotId = reader.GetString(3); ProductionIdentifier(snapshotId, 1024);
            var station = reader.GetString(4); ValidateReportId(station);
            var status = reader.GetString(5);
            if (status is not ("PrintQueued" or "Dispatching" or "PrintDispatched" or "Failed" or "Unknown" or "Completed"))
            { throw InvalidHistoryPrint(); }
            string? original = null;
            if (!reader.IsDBNull(9))
            {
                if (reader.GetValue(9) is not string) { throw InvalidHistoryPrint(); }
                original = reader.GetString(9); ProductionIdentifier(original, 1024);
            }
            if (snapshotId != snapshot.SnapshotId || station != snapshot.ProductionStationId || reprint != 0 ||
                original is not null || request != snapshot.SubmittedRequestId) { throw InvalidHistoryPrint(); }
            return new(id, request, snapshotId, station, status, (int)count, ProductionTimestamp(reader.GetString(7)), reprint == 1, original);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Recorded print evidence is unavailable.", error); }
    }

    private static CashHistoryPrintAttempt ReadHistoryPrintAttempt(SqliteDataReader reader, int attemptCount)
    {
        try
        {
            foreach (var index in new[] { 1, 3, 5 }) { if (reader.GetValue(index) is not string) { throw InvalidHistoryPrint(); } }
            if (reader.GetValue(2) is not long number || number is < 1 or > int.MaxValue || number > attemptCount ||
                reader.GetValue(4) is not long retry || retry is not (0 or 1)) { throw InvalidHistoryPrint(); }
            var id = reader.GetString(1); ProductionIdentifier(id, 1024);
            var status = reader.GetString(3);
            if (status is not ("Connecting" or "Dispatching" or "PrintDispatched" or "Failed" or "Unknown")) { throw InvalidHistoryPrint(); }
            DateTimeOffset? NullableTime(int index)
            {
                if (reader.IsDBNull(index)) { return null; }
                if (reader.GetValue(index) is not string) { throw InvalidHistoryPrint(); }
                return ProductionTimestamp(reader.GetString(index));
            }
            var started = ProductionTimestamp(reader.GetString(5)); var firstByte = NullableTime(6); var finished = NullableTime(7);
            var terminal = status is "PrintDispatched" or "Failed" or "Unknown";
            if (terminal != finished.HasValue ||
                (status is "Dispatching" or "PrintDispatched" && firstByte is null) ||
                (status is "Connecting" or "Failed" && firstByte is not null) ||
                (retry == 1 && (status != "Failed" || firstByte is not null))) { throw InvalidHistoryPrint(); }
            return new(id, (int)number, status, retry == 1, started, firstByte, finished);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Recorded print evidence is unavailable.", error); }
    }

    private static void ValidateHistoryPrintQuery(CashHistoryPrintQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 50) { throw new ArgumentException("Invalid print evidence query."); }
    }
    private async Task<long> HistoryPrintSchemaCookie(CancellationToken token)
    {
        using var read = CreateCommand("PRAGMA schema_version;");
        return Convert.ToInt64(await read.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }
    private static void BindHistoryPrintSubject(SqliteCommand command, string branchId, string sessionId, string snapshotId)
    {
        command.Parameters.AddWithValue("$branch", branchId); command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$snapshot", snapshotId);
    }
    private static void BindHistoryPrintPage(SqliteCommand command, int limit, long high, long? before)
    {
        command.Parameters.AddWithValue("$limit", limit + 1); command.Parameters.AddWithValue("$high", high);
        command.Parameters.AddWithValue("$before", (object?)before ?? DBNull.Value);
    }
    private static string EncodeHistoryPrintCursor(HistoryPrintCursor value) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string HistoryPrintScopeHash(string branchId, string requestId, string sessionId, string snapshotId,
        string source, string? jobId) => Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { branchId, requestId, sessionId, snapshotId, source, jobId })));
    private static HistoryPrintCursor DecodeHistoryPrintCursor(string value, string branchId, string requestId,
        string sessionId, string snapshotId, string source, string? jobId, int limit, long cookie)
    {
        try
        {
            if (value.Length is < 1 or > 4096 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            { throw new ArgumentException("Invalid print evidence cursor."); }
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
            if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value)
            { throw new ArgumentException("Invalid print evidence cursor."); }
            using var document = JsonDocument.Parse(bytes);
            var names = new HashSet<string>(["Version", "ScopeHash", "Limit", "SchemaCookie", "HighWater", "Before"], StringComparer.Ordinal);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != names.Count ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != names.Count ||
                root.EnumerateObject().Any(property => !names.Contains(property.Name))) { throw new ArgumentException("Invalid print evidence cursor."); }
            var cursor = JsonSerializer.Deserialize<HistoryPrintCursor>(bytes) ?? throw new ArgumentException("Invalid print evidence cursor.");
            if (cursor.Version != 1 || cursor.ScopeHash != HistoryPrintScopeHash(branchId, requestId, sessionId, snapshotId, source, jobId) || cursor.Limit != limit ||
                cursor.SchemaCookie != cookie || cursor.Before < 1 || cursor.HighWater < cursor.Before)
            { throw new ArgumentException("Print evidence cursor does not match the query."); }
            return cursor;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new ArgumentException("Invalid print evidence cursor.", error); }
    }
    private static InvalidDataException InvalidHistoryPrint() => new("Recorded print evidence is unavailable.");
}
