using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pos.Application.Synchronization;

namespace Pos.Application.Tests;

public sealed class ClosingReportEnvelopeTests
{
    private static string Payload(string operation = "CloseStore", string branch = "branch", string request = "request", bool uncounted = false)
    {
        var reportId = "closing:" + Convert.ToHexString(Encoding.UTF8.GetBytes(branch)) + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(request));
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            reportId,
            branchId = branch,
            requestId = request,
            storeSessionId = "store",
            shiftId = "shift",
            operation,
            businessDate = "2026-11-20",
            closing = new
            {
                StoreSessionId = "store",
                ShiftId = "shift",
                ClosesStore = operation == "CloseStore",
                ExpectedCashAmount = 500m,
                ActualCashAmount = uncounted ? (decimal?)null : 490m,
                CashDifference = uncounted ? (decimal?)null : -10m,
                ReconciliationStatus = uncounted ? "NotReconciled" : "Reconciled",
                Context = new
                {
                    EmployeeId = "employee",
                    DeviceId = "device",
                    OccurredAt = "2026-11-20T03:00:00.1234567+00:00",
                    BranchLocalDate = "2026-11-20"
                }
            },
            cashSummary = new
            {
                Source = "CashSales",
                Store = new { ReceiptCount = 1, AppliedCashAmount = 0.1m, Financials = new { VatAmount = 0.01m } },
                Tables = new { OpenCount = 2, AwaitingPaymentCount = 1 }
            }
        });
    }

    private static string Change(string payload, string path, string json)
    {
        var root = JsonNode.Parse(payload)!;
        var parts = path.Split('.');
        var current = root;
        foreach (var part in parts[..^1]) { current = current[part]!; }
        current[parts[^1]] = JsonNode.Parse(json);
        return root.ToJsonString();
    }

    [Theory]
    [InlineData("CloseStore", false)]
    [InlineData("CloseShift", false)]
    [InlineData("CloseShift", true)]
    public void Accepted_closings_preserve_the_complete_original_snapshot(string operation, bool uncounted)
    {
        var payload = " \n" + Payload(operation, uncounted: uncounted) + "\n";
        var report = ClosingReportEnvelope.Parse(payload);
        Assert.Equal(payload, report.PayloadJson);
        Assert.Equal("branch", report.BranchId);
        Assert.Equal("request", report.RequestId);
        Assert.Equal("store", report.StoreSessionId);
        Assert.Equal("shift", report.ShiftId);
        Assert.Equal(operation, report.Operation);
        Assert.Equal(new DateOnly(2026, 11, 20), report.BusinessDate);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T03:00:00.1234567Z"), report.OccurredAt);
    }

    [Fact]
    public void Legacy_missing_cash_snapshot_remains_null_without_reconstruction()
    {
        var payload = Change(Payload(), "cashSummary", "null");
        Assert.Equal(payload, ClosingReportEnvelope.Parse(payload).PayloadJson);
    }

    [Fact]
    public void Utf8_report_identity_is_stable_and_separator_safe()
    {
        var report = ClosingReportEnvelope.Parse(Payload(branch: "สาขา:หนึ่ง", request: "คำขอ:หนึ่ง"));
        Assert.Equal("สาขา:หนึ่ง", report.BranchId);
        Assert.Equal("คำขอ:หนึ่ง", report.RequestId);
        Assert.Equal("closing:" + Convert.ToHexString(Encoding.UTF8.GetBytes(report.BranchId)) + ":" +
            Convert.ToHexString(Encoding.UTF8.GetBytes(report.RequestId)), report.ReportId);
        Assert.NotEqual(ClosingReportEnvelope.Parse(Payload(branch: "a:b", request: "c")).ReportId,
            ClosingReportEnvelope.Parse(Payload(branch: "a", request: "b:c")).ReportId);
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("schemaVersion", "\"1\"")]
    [InlineData("schemaVersion", "null")]
    [InlineData("schemaVersion", "1.5")]
    [InlineData("reportId", "\"foreign\"")]
    [InlineData("reportId", "null")]
    [InlineData("operation", "\"OpenShift\"")]
    [InlineData("operation", "\"closeshift\"")]
    [InlineData("businessDate", "\"2026-02-29\"")]
    [InlineData("businessDate", "\"2026-1-1\"")]
    [InlineData("closing", "null")]
    [InlineData("closing.StoreSessionId", "\"foreign\"")]
    [InlineData("closing.ShiftId", "\"foreign\"")]
    [InlineData("closing.ClosesStore", "false")]
    [InlineData("closing.ClosesStore", "\"true\"")]
    [InlineData("closing.ExpectedCashAmount", "-1")]
    [InlineData("closing.ExpectedCashAmount", "500.001")]
    [InlineData("closing.ExpectedCashAmount", "1e100")]
    [InlineData("closing.ExpectedCashAmount", "null")]
    [InlineData("closing.ActualCashAmount", "-1")]
    [InlineData("closing.ActualCashAmount", "490.001")]
    [InlineData("closing.CashDifference", "10")]
    [InlineData("closing.CashDifference", "null")]
    [InlineData("closing.ReconciliationStatus", "\"NotReconciled\"")]
    [InlineData("closing.Context", "[]")]
    [InlineData("closing.Context.EmployeeId", "\"\"")]
    [InlineData("closing.Context.DeviceId", "\" device\"")]
    [InlineData("closing.Context.BranchLocalDate", "\"2026-02-30\"")]
    [InlineData("closing.Context.OccurredAt", "\"2026-11-20T03:00:00+07:00\"")]
    [InlineData("closing.Context.OccurredAt", "\"2026-11-20T03:00:00\"")]
    [InlineData("closing.Context.OccurredAt", "\"2026-02-30T03:00:00Z\"")]
    [InlineData("cashSummary", "[]")]
    public void Inconsistent_or_unsupported_report_fields_are_rejected(string path, string replacement)
    {
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(Change(Payload(), path, replacement)));
    }

    [Theory]
    [InlineData("branchId")]
    [InlineData("requestId")]
    [InlineData("storeSessionId")]
    [InlineData("shiftId")]
    public void Missing_blank_control_or_oversized_identity_is_rejected(string field)
    {
        var root = JsonNode.Parse(Payload())!.AsObject();
        root.Remove(field);
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(root.ToJsonString()));
        foreach (var value in new[] { "", " ", "id\ncontrol", new string('x', 257) })
        {
            Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(Change(Payload(), field, JsonSerializer.Serialize(value))));
        }
    }

    [Fact]
    public void Count_and_difference_must_be_present_together_and_store_closing_requires_a_count()
    {
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(Payload(uncounted: true)));
        var shift = Payload("CloseShift", uncounted: true);
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(Change(shift, "closing.CashDifference", "0")));
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(Change(shift, "closing.ReconciliationStatus", "\"Reconciled\"")));
    }

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")]
    [InlineData("\"ExpectedCashAmount\":500", "\"ExpectedCashAmount\":500,\"ExpectedCashAmount\":500")]
    [InlineData("\"EmployeeId\":\"employee\"", "\"EmployeeId\":\"employee\",\"EmployeeId\":\"employee\"")]
    [InlineData("\"VatAmount\":0.01", "\"VatAmount\":0.01,\"VatAmount\":0.01")]
    public void Duplicate_properties_are_rejected_at_every_snapshot_level(string target, string replacement)
    {
        var payload = Payload();
        Assert.Contains(target, payload, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(payload.Replace(target, replacement, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    public void Non_object_report_is_rejected(string payload) =>
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(payload));

    [Fact]
    public void Malformed_json_and_excessive_depth_are_rejected()
    {
        Assert.ThrowsAny<JsonException>(() => ClosingReportEnvelope.Parse("{"));
        var deep = Change(Payload(), "cashSummary", new string('[', 33) + "0" + new string(']', 33));
        Assert.ThrowsAny<JsonException>(() => ClosingReportEnvelope.Parse(deep));
    }

    [Theory]
    [InlineData("2026-11-20 03:00:00Z")]
    [InlineData("2026-11-20T03:00:00.12345678Z")]
    public void Timestamp_must_use_canonical_iso_wire_format(string timestamp) =>
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(Change(Payload(), "closing.Context.OccurredAt", JsonSerializer.Serialize(timestamp))));

    [Theory]
    [InlineData("EmployeeId")]
    [InlineData("OccurredAt")]
    public void Invalid_unicode_string_is_rejected_as_an_invalid_report(string field)
    {
        var payload = Change(Payload(), "closing.Context." + field, "\"placeholder\"");
        payload = payload.Replace("\"placeholder\"", "\"\\uD800\"", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(payload));
    }

    [Theory]
    [InlineData("\"Source\":", "\"\\uD800\":")]
    [InlineData("\"CashSales\"", "\"\\uD800\"")]
    [InlineData("\"CashSales\"", "[\"\\uD800\"]")]
    public void Invalid_unicode_in_opaque_summary_names_or_values_is_rejected(string target, string replacement)
    {
        var payload = Payload().Replace(target, replacement, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(payload));
    }

    [Fact]
    public void Long_unicode_identity_is_accepted_within_the_index_safe_derived_report_limit()
    {
        var report = ClosingReportEnvelope.Parse(Payload(branch: new string('ส', 165), request: new string('ข', 166)));
        Assert.Equal(1995, report.ReportId.Length);
        Assert.Equal(165, report.BranchId.Length);
        Assert.Equal(166, report.RequestId.Length);
    }

    [Fact]
    public void Derived_report_identity_that_exceeds_index_safe_size_is_rejected_even_when_ids_are_short_enough()
    {
        var payload = Payload(branch: new string('ส', 166), request: new string('ข', 166));
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(2001, document.RootElement.GetProperty("reportId").GetString()!.Length);
        Assert.Throws<ArgumentException>(() => ClosingReportEnvelope.Parse(payload));
    }
}
