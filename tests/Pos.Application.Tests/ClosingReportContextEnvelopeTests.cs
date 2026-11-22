using System.Text.Json;
using System.Text.Json.Nodes;
using Pos.Application.Synchronization;
using Pos.Domain.StoreSessions;

namespace Pos.Application.Tests;

public sealed class ClosingReportContextEnvelopeTests
{
    private static readonly DateOnly Day = new(2026, 11, 22);
    private static readonly StoreOperationContext Opened = new("opener", "device", new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero), Day);
    private static readonly ShiftClosing Close = new("store", "shift", true, 500, 490, -10,
        new("closer", "device", new DateTimeOffset(2026, 11, 22, 4, 0, 0, TimeSpan.Zero).AddTicks(1), Day));
    private const string ReportId = "closing:6272616E6368:72657175657374";
    private static string Payload() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        contextId = "lifecycle:" + ReportId,
        reportId = ReportId,
        branchId = "branch",
        requestId = "request",
        context = new
        {
            SchemaVersion = 1,
            BranchId = "branch",
            RequestId = "request",
            StoreSessionId = "store",
            ClosingShiftId = "shift",
            BusinessDate = Day,
            OpeningCashAmount = 500m,
            Opened,
            ClosesStore = true,
            Shifts = new[] { new { ShiftId = "shift", OpeningCashAmount = 500m, Opened, Closing = Close } }
        }
    });
    private static string Parent() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        reportId = ReportId,
        branchId = "branch",
        requestId = "request",
        storeSessionId = "store",
        shiftId = "shift",
        operation = "CloseStore",
        businessDate = Day,
        closing = Close,
        cashSummary = (object?)null
    });
    private static string Change(string path, string replacement)
    {
        var root = JsonNode.Parse(Payload())!; var parts = path.Split('.'); var current = root;
        foreach (var part in parts[..^1]) { current = current[part]!; }
        current[parts[^1]] = JsonNode.Parse(replacement); return root.ToJsonString();
    }
    [Fact]
    public void Complete_context_retains_original_bytes_and_matches_parent()
    {
        var raw = Payload(); var parsed = ClosingReportContextEnvelope.Parse(raw);
        Assert.Equal(raw, parsed.PayloadJson); Assert.Equal("lifecycle:" + ReportId, parsed.ContextId);
        Assert.Equal("store", parsed.StoreSessionId); Assert.True(parsed.MatchesParent(Parent()));
        Assert.Equal(1, parsed.TerminalClosing.Context.OccurredAt.Ticks % TimeSpan.TicksPerSecond);
    }
    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("contextId", "\"other\"")]
    [InlineData("branchId", "\"other\"")]
    [InlineData("requestId", "\"other\"")]
    [InlineData("context.SchemaVersion", "2")]
    [InlineData("context.BranchId", "\"other\"")]
    [InlineData("context.RequestId", "\"other\"")]
    [InlineData("context.ClosingShiftId", "\"other\"")]
    [InlineData("context.BusinessDate", "\"2026-02-30\"")]
    [InlineData("context.OpeningCashAmount", "-1")]
    [InlineData("context.Opened.BranchLocalDate", "\"2026-11-21\"")]
    [InlineData("context.Opened.OccurredAt", "\"2026-11-22T10:00:00+07:00\"")]
    [InlineData("context.Opened.OccurredAt", "\"2026-11-22 03:00:00Z\"")]
    [InlineData("context.Shifts", "[]")]
    [InlineData("context.ClosesStore", "false")]
    public void Invalid_context_is_rejected(string path, string replacement) =>
        Assert.Throws<ArgumentException>(() => ClosingReportContextEnvelope.Parse(Change(path, replacement)));
    [Theory]
    [InlineData("opening")]
    [InlineData("closing")]
    [InlineData("duplicate")]
    [InlineData("difference")]
    [InlineData("count")]
    public void Shift_integrity_is_enforced(string change)
    {
        var root = JsonNode.Parse(Payload())!; var shifts = root["context"]!["Shifts"]!.AsArray(); var first = shifts[0]!;
        switch (change)
        {
            case "opening": first["OpeningCashAmount"] = 0; break;
            case "closing": first["Closing"]!["Context"]!["BranchLocalDate"] = "2026-11-21"; break;
            case "duplicate": shifts.Add(first.DeepClone()); break;
            case "difference": first["Closing"]!["CashDifference"] = 0; break;
            case "count": first["Closing"]!["ActualCashAmount"] = null; break;
        }
        Assert.Throws<ArgumentException>(() => ClosingReportContextEnvelope.Parse(root.ToJsonString()));
    }
    [Fact]
    public void Duplicate_nested_properties_and_bad_unicode_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ClosingReportContextEnvelope.Parse(Payload().Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"SchemaVersion\":1")));
        Assert.Throws<ArgumentException>(() => ClosingReportContextEnvelope.Parse(Payload().Replace("opener", "\\uD800")));
        Assert.Throws<ArgumentException>(() => ClosingReportContextEnvelope.Parse(Payload().Replace("\"context\":{", "\"context\":{\"\\uD800\":1,")));
    }
    [Fact]
    public void Non_monotonic_clock_and_informational_subcent_float_do_not_add_business_rules()
    {
        var root = JsonNode.Parse(Payload())!;
        root["context"]!["Opened"]!["OccurredAt"] = "2026-11-22T05:00:00Z";
        root["context"]!["Shifts"]![0]!["Opened"]!["OccurredAt"] = "2026-11-22T05:00:00Z";
        root["context"]!["OpeningCashAmount"] = 500.001m;
        root["context"]!["Shifts"]![0]!["OpeningCashAmount"] = 500.001m;
        Assert.NotNull(ClosingReportContextEnvelope.Parse(root.ToJsonString()));
    }
    [Fact]
    public void Parent_identity_or_terminal_closing_change_is_a_conflict()
    {
        var parsed = ClosingReportContextEnvelope.Parse(Payload()); var root = JsonNode.Parse(Parent())!;
        root["closing"]!["ActualCashAmount"] = 480; root["closing"]!["CashDifference"] = -20;
        Assert.False(parsed.MatchesParent(root.ToJsonString()));
        root = JsonNode.Parse(Parent())!; root["storeSessionId"] = "other"; root["closing"]!["StoreSessionId"] = "other";
        Assert.False(parsed.MatchesParent(root.ToJsonString()));
    }
    [Fact]
    public void Uncounted_shift_context_matches_uncounted_parent()
    {
        var root = JsonNode.Parse(Payload())!; root["context"]!["ClosesStore"] = false;
        var closing = root["context"]!["Shifts"]![0]!["Closing"]!;
        closing["ClosesStore"] = false; closing["ActualCashAmount"] = null; closing["CashDifference"] = null; closing["ReconciliationStatus"] = "NotReconciled";
        var parent = JsonNode.Parse(Parent())!; parent["operation"] = "CloseShift"; parent["closing"] = closing.DeepClone();
        Assert.True(ClosingReportContextEnvelope.Parse(root.ToJsonString()).MatchesParent(parent.ToJsonString()));
    }
}
