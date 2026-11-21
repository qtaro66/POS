using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pos.Application.Synchronization;

public enum ClosingReportPersistenceResult { Accepted, AlreadyAccepted, Conflict }

public interface IClosingReportIngestionStore
{
    Task<ClosingReportPersistenceResult> PersistAsync(ClosingReportEnvelope report, CancellationToken token = default);
}

public sealed class ClosingReportStoreUnavailableException : Exception
{
    public ClosingReportStoreUnavailableException(Exception inner) : base("Closing report storage is unavailable.", inner) { }
}

public sealed record ClosingReportEnvelope
{
    private ClosingReportEnvelope(string reportId, string branchId, string requestId, string storeSessionId,
        string shiftId, string operation, DateOnly businessDate, DateTimeOffset occurredAt, string payloadJson)
    {
        ReportId = reportId; BranchId = branchId; RequestId = requestId; StoreSessionId = storeSessionId;
        ShiftId = shiftId; Operation = operation; BusinessDate = businessDate; OccurredAt = occurredAt; PayloadJson = payloadJson;
    }
    public string ReportId { get; }
    public string BranchId { get; }
    public string RequestId { get; }
    public string StoreSessionId { get; }
    public string ShiftId { get; }
    public string Operation { get; }
    public DateOnly BusinessDate { get; }
    public DateTimeOffset OccurredAt { get; }
    public string PayloadJson { get; }

    public static ClosingReportEnvelope Parse(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        RequireObject(root);
        RequireUniqueProperties(root);
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number != 1) { throw Invalid(); }
        var branch = Id(root, "branchId");
        var request = Id(root, "requestId");
        var store = Id(root, "storeSessionId");
        var shift = Id(root, "shiftId");
        var report = Text(root, "reportId");
        if (report.Length > 2000 || report != "closing:" + Convert.ToHexString(Encoding.UTF8.GetBytes(branch)) + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(request))) { throw Invalid(); }
        var operation = Text(root, "operation");
        if (operation is not ("CloseShift" or "CloseStore")) { throw Invalid(); }
        if (!DateOnly.TryParseExact(Text(root, "businessDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) { throw Invalid(); }
        var closing = Property(root, "closing");
        RequireObject(closing);
        var closesStore = Property(closing, "ClosesStore");
        if (closesStore.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            closesStore.GetBoolean() != (operation == "CloseStore") ||
            Text(closing, "StoreSessionId") != store || Text(closing, "ShiftId") != shift) { throw Invalid(); }
        var expected = Money(closing, "ExpectedCashAmount", false)!.Value;
        var actual = Money(closing, "ActualCashAmount", true);
        var difference = Money(closing, "CashDifference", true, allowNegative: true);
        if ((operation == "CloseStore" && actual is null) || difference != (actual - expected)) { throw Invalid(); }
        if (closing.TryGetProperty("ReconciliationStatus", out _) &&
            Text(closing, "ReconciliationStatus") != (actual is null ? "NotReconciled" : "Reconciled")) { throw Invalid(); }
        var context = Property(closing, "Context");
        RequireObject(context);
        Id(context, "EmployeeId");
        Id(context, "DeviceId");
        if (!DateOnly.TryParseExact(Text(context, "BranchLocalDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) { throw Invalid(); }
        var timestamp = Text(context, "OccurredAt");
        if (!Regex.IsMatch(timestamp, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) ||
            !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred) || occurred.Offset != TimeSpan.Zero) { throw Invalid(); }
        var summary = Property(root, "cashSummary");
        if (summary.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) { throw Invalid(); }
        return new(report, branch, request, store, shift, operation, day, occurred, payloadJson);
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
    private static decimal? Money(JsonElement root, string name, bool nullable, bool allowNegative = false)
    {
        var value = Property(root, name);
        if (nullable && value.ValueKind == JsonValueKind.Null) { return null; }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount) ||
            (!allowNegative && amount < 0) || decimal.Round(amount, 2) != amount) { throw Invalid(); }
        return amount;
    }
    private static void RequireObject(JsonElement value) { if (value.ValueKind != JsonValueKind.Object) { throw Invalid(); } }
    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                try { if (!names.Add(property.Name)) { throw Invalid(); } }
                catch (InvalidOperationException) { throw Invalid(); }
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            try { _ = value.GetString(); }
            catch (InvalidOperationException) { throw Invalid(); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) { RequireUniqueProperties(item); }
        }
    }
    private static ArgumentException Invalid() => new("Invalid closing report envelope.");
}
