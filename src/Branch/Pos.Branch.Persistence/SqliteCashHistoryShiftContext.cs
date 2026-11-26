using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<CashHistoryShiftContext?> ReadCashHistoryShiftContextAsync(string branchId, string requestId,
        DateTimeOffset now, CancellationToken token = default) => ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadCashHistoryShiftContextAsync(branchId, requestId, now, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<CashHistoryShiftContext?> ReadCashHistoryShiftContextAsync(string branchId, string requestId,
        DateTimeOffset now, CancellationToken token)
    {
        ValidateReportId(branchId); ValidateReportId(requestId); RequireClosingReportUtc(now);
        CashHistoryItem subject;
        using (var parent = CreateCommand($"SELECT {CashHistoryColumns} FROM {CashHistoryJoin} WHERE sale.branch_id=$branch AND sale.request_id=$request;"))
        {
            parent.Parameters.AddWithValue("$branch", branchId); parent.Parameters.AddWithValue("$request", requestId);
            await using var reader = await parent.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) { return null; }
            subject = ReadCashHistoryItem(reader);
        }
        using var read = CreateCommand("""
            SELECT request_id,store_session_id,command_json,result_json,typeof(command_json),typeof(result_json) FROM store_lifecycle_commands
            WHERE branch_id=$branch AND store_session_id=$store AND json_extract(command_json,'$.ShiftId')=$shift
            ORDER BY sequence LIMIT 3;
            """);
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$store", subject.StoreSessionId);
        read.Parameters.AddWithValue("$shift", subject.ShiftId);
        var entries = new List<CashHistoryShiftEntry>();
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                if (entries.Count == 2) { throw InvalidHistoryShift(); }
                var entry = ReadHistoryShiftEntry(reader, subject);
                var closing = entry.Closing is not null;
                if (entries.Any(previous => (previous.Closing is not null) == closing) || (!closing && entries.Count != 0))
                { throw InvalidHistoryShift(); }
                entries.Add(entry);
            }
        }
        return new(1, branchId, now, requestId, subject.TableSessionId, subject.StoreSessionId, subject.ShiftId,
            subject.BusinessDate, entries.ToArray());
    }

    private static CashHistoryShiftEntry ReadHistoryShiftEntry(SqliteDataReader reader, CashHistoryItem subject)
    {
        try
        {
            if (reader.GetString(4) != "text" || reader.GetString(5) != "text") { throw InvalidHistoryShift(); }
            var commandJson = reader.GetString(2); var resultJson = reader.GetString(3);
            if (Encoding.UTF8.GetByteCount(commandJson) > 1024 * 1024 || Encoding.UTF8.GetByteCount(resultJson) > 1024 * 1024)
            { throw InvalidHistoryShift(); }
            using var commandDocument = JsonDocument.Parse(commandJson); using var resultDocument = JsonDocument.Parse(resultJson);
            var command = commandDocument.RootElement; var result = resultDocument.RootElement;
            CashEvidenceObject(command); CashEvidenceObject(result);
            var request = HistoryShiftId(command.GetProperty("RequestId"));
            if (request != reader.GetString(0) || HistoryShiftId(command.GetProperty("StoreSessionId")) != subject.StoreSessionId ||
                reader.GetString(1) != subject.StoreSessionId || HistoryShiftId(command.GetProperty("ShiftId")) != subject.ShiftId ||
                HistoryShiftId(result.GetProperty("StoreSessionId")) != subject.StoreSessionId ||
                HistoryShiftDate(result.GetProperty("BusinessDate")) != subject.BusinessDate)
            { throw InvalidHistoryShift(); }
            var action = command.GetProperty("Action").GetInt32() switch
            { 0 => "OpenStore", 1 => "OpenShift", 2 => "CloseShift", 3 => "CloseStore", _ => throw InvalidHistoryShift() };
            var context = HistoryShiftContext(command.GetProperty("Context"));
            var cash = HistoryShiftNullableAmount(command.GetProperty("CashAmount"));
            var openingCash = result.GetProperty("OpeningCashAmount").GetDecimal();
            if (openingCash < 0 || cash < 0) { throw InvalidHistoryShift(); }
            var resultClosed = result.GetProperty("IsClosed").GetBoolean();
            var resultActive = result.GetProperty("ActiveShiftId");
            var resultClosing = result.GetProperty("Closing");
            CashHistoryShiftClosing? closing = null;
            if (action is "OpenStore" or "OpenShift")
            {
                if (resultClosed || HistoryShiftId(resultActive) != subject.ShiftId ||
                    (action == "OpenStore" && (cash is null || cash != openingCash ||
                        context.BranchLocalDate != subject.BusinessDate || resultClosing.ValueKind != JsonValueKind.Null)))
                { throw InvalidHistoryShift(); }
                // OpenShift results retain the prior shift's closing. It is not evidence for this shift.
            }
            else
            {
                CashEvidenceObject(resultClosing);
                if (resultActive.ValueKind != JsonValueKind.Null || resultClosed != (action == "CloseStore") ||
                    HistoryShiftId(resultClosing.GetProperty("StoreSessionId")) != subject.StoreSessionId ||
                    HistoryShiftId(resultClosing.GetProperty("ShiftId")) != subject.ShiftId ||
                    HistoryShiftContext(resultClosing.GetProperty("Context")) != context)
                { throw InvalidHistoryShift(); }
                var closesStore = resultClosing.GetProperty("ClosesStore").GetBoolean();
                var expected = resultClosing.GetProperty("ExpectedCashAmount").GetDecimal();
                var actual = HistoryShiftNullableAmount(resultClosing.GetProperty("ActualCashAmount"));
                var difference = HistoryShiftNullableAmount(resultClosing.GetProperty("CashDifference"));
                if (closesStore != (action == "CloseStore") || expected < 0 || actual < 0 || actual != cash ||
                    (actual.HasValue != difference.HasValue) || (actual.HasValue && difference != actual - expected) ||
                    (closesStore && actual is null)) { throw InvalidHistoryShift(); }
                closing = new(closesStore, expected, actual, difference);
            }
            return new(request, action, context.OccurredAt, context.BranchLocalDate, context.EmployeeId, context.DeviceId, cash, closing);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or OverflowException or FormatException)
        { throw new InvalidDataException("Saved cash shift context is unavailable.", error); }
    }

    private sealed record HistoryShiftOperationContext(string EmployeeId, string DeviceId, DateTimeOffset OccurredAt, string BranchLocalDate);
    private static HistoryShiftOperationContext HistoryShiftContext(JsonElement value)
    {
        CashEvidenceObject(value);
        return new(HistoryShiftId(value.GetProperty("EmployeeId")), HistoryShiftId(value.GetProperty("DeviceId")),
            ProductionTimestamp(value.GetProperty("OccurredAt").GetString()!), HistoryShiftDate(value.GetProperty("BranchLocalDate")));
    }
    private static string HistoryShiftId(JsonElement value)
    {
        var id = value.GetString()!; ValidateReportId(id); return id;
    }
    private static string HistoryShiftDate(JsonElement value)
    {
        var date = value.GetString()!;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        { throw InvalidHistoryShift(); }
        return date;
    }
    private static decimal? HistoryShiftNullableAmount(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
    private static InvalidDataException InvalidHistoryShift() => new("Saved cash shift context is unavailable.");
}
