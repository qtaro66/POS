using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pos.Domain.StoreSessions;

namespace Pos.Application.Synchronization;

public enum ClosingReportContextPersistenceResult { Accepted, AlreadyAccepted, Conflict, ParentMissing }
public interface IClosingReportContextIngestionStore
{
    Task<ClosingReportContextPersistenceResult> PersistAsync(ClosingReportContextEnvelope context, CancellationToken token = default);
}
public sealed record ClosingReportContextEnvelope
{
    private ClosingReportContextEnvelope(string contextId, string reportId, string branchId, string requestId,
        string storeSessionId, string shiftId, DateOnly businessDate, ShiftClosing terminalClosing, string payloadJson)
    {
        ContextId = contextId; ReportId = reportId; BranchId = branchId; RequestId = requestId;
        StoreSessionId = storeSessionId; ShiftId = shiftId; BusinessDate = businessDate;
        TerminalClosing = terminalClosing; PayloadJson = payloadJson;
    }
    public string ContextId { get; }
    public string ReportId { get; }
    public string BranchId { get; }
    public string RequestId { get; }
    public string StoreSessionId { get; }
    public string ShiftId { get; }
    public DateOnly BusinessDate { get; }
    public ShiftClosing TerminalClosing { get; }
    public string PayloadJson { get; }

    public static ClosingReportContextEnvelope Parse(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        Object(root); Unique(root);
        Version(root, "schemaVersion");
        var branch = Id(root, "branchId"); var request = Id(root, "requestId");
        var report = Text(root, "reportId"); var id = Text(root, "contextId");
        if (report.Length > 2000 || report != "closing:" + Convert.ToHexString(Encoding.UTF8.GetBytes(branch)) + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(request)) || id != "lifecycle:" + report) { throw Invalid(); }
        var context = Property(root, "context"); Object(context); Version(context, "SchemaVersion");
        var store = Id(context, "StoreSessionId"); var shiftId = Id(context, "ClosingShiftId");
        var day = Day(context, "BusinessDate");
        if (Text(context, "BranchId") != branch || Text(context, "RequestId") != request) { throw Invalid(); }
        var openingCash = Money(context, "OpeningCashAmount", false)!.Value;
        var opened = Attribution(Property(context, "Opened"), day);
        var closes = Boolean(context, "ClosesStore");
        var shifts = Property(context, "Shifts");
        if (shifts.ValueKind != JsonValueKind.Array || shifts.GetArrayLength() == 0) { throw Invalid(); }
        var identities = new HashSet<string>(StringComparer.Ordinal);
        ShiftClosing? final = null;
        var index = 0;
        foreach (var shift in shifts.EnumerateArray())
        {
            Object(shift);
            var currentId = Id(shift, "ShiftId");
            if (!identities.Add(currentId)) { throw Invalid(); }
            var cash = Money(shift, "OpeningCashAmount", true);
            var currentOpened = Attribution(Property(shift, "Opened"), day);
            if (index == 0 && (cash != openingCash || currentOpened != opened)) { throw Invalid(); }
            var last = index == shifts.GetArrayLength() - 1;
            final = Closing(Property(shift, "Closing"), store, currentId, closes && last, day);
            if (last && currentId != shiftId) { throw Invalid(); }
            index++;
        }
        return new(id, report, branch, request, store, shiftId, day, final!, payloadJson);
    }

    public bool MatchesParent(string parentPayloadJson)
    {
        var parent = ClosingReportEnvelope.Parse(parentPayloadJson);
        if (parent.ReportId != ReportId || parent.BranchId != BranchId || parent.RequestId != RequestId ||
            parent.StoreSessionId != StoreSessionId || parent.ShiftId != ShiftId || parent.BusinessDate != BusinessDate ||
            parent.Operation != (TerminalClosing.ClosesStore ? "CloseStore" : "CloseShift")) { return false; }
        using var document = JsonDocument.Parse(parentPayloadJson);
        return Closing(Property(document.RootElement, "closing"), StoreSessionId, ShiftId,
            TerminalClosing.ClosesStore, BusinessDate) == TerminalClosing;
    }

    private static ShiftClosing Closing(JsonElement value, string store, string shift, bool closesStore, DateOnly day)
    {
        Object(value);
        if (Text(value, "StoreSessionId") != store || Text(value, "ShiftId") != shift || Boolean(value, "ClosesStore") != closesStore) { throw Invalid(); }
        var expected = Money(value, "ExpectedCashAmount", false)!.Value;
        var actual = Money(value, "ActualCashAmount", true);
        var difference = Money(value, "CashDifference", true, true);
        if ((closesStore && actual is null) || difference != actual - expected ||
            (value.TryGetProperty("ReconciliationStatus", out _) && Text(value, "ReconciliationStatus") != (actual is null ? "NotReconciled" : "Reconciled"))) { throw Invalid(); }
        return new(store, shift, closesStore, expected, actual, difference, Attribution(Property(value, "Context"), day));
    }
    private static StoreOperationContext Attribution(JsonElement value, DateOnly day)
    {
        Object(value);
        if (Day(value, "BranchLocalDate") != day) { throw Invalid(); }
        var stamp = Text(value, "OccurredAt");
        if (!Regex.IsMatch(stamp, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) ||
            !DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) || timestamp.Offset != TimeSpan.Zero) { throw Invalid(); }
        return new(Id(value, "EmployeeId"), Id(value, "DeviceId"), timestamp, day);
    }
    private static DateOnly Day(JsonElement root, string name) =>
        DateOnly.TryParseExact(Text(root, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Invalid();
    private static decimal? Money(JsonElement root, string name, bool nullable, bool signed = false)
    {
        var value = Property(root, name);
        if (nullable && value.ValueKind == JsonValueKind.Null) { return null; }
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount) && (signed || amount >= 0) ? amount : throw Invalid();
    }
    private static bool Boolean(JsonElement root, string name)
    {
        var value = Property(root, name);
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Invalid();
    }
    private static void Version(JsonElement root, string name)
    {
        var version = Property(root, name);
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1) { throw Invalid(); }
    }
    private static JsonElement Property(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value : throw Invalid();
    private static string Text(JsonElement root, string name)
    {
        var value = Property(root, name);
        try { return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
    }
    private static string Id(JsonElement root, string name)
    {
        var value = Text(root, name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value != value.Trim() || value.Any(char.IsControl)) { throw Invalid(); }
        return value;
    }
    private static void Object(JsonElement value) { if (value.ValueKind != JsonValueKind.Object) { throw Invalid(); } }
    private static void Unique(JsonElement value)
    {
        try
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) { throw Invalid(); }
                    Unique(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.String) { _ = value.GetString(); }
            else if (value.ValueKind == JsonValueKind.Array) { foreach (var child in value.EnumerateArray()) { Unique(child); } }
        }
        catch (InvalidOperationException) { throw Invalid(); }
    }
    private static ArgumentException Invalid() => new("Invalid closing report lifecycle envelope.");
}
