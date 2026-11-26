using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private static Task<string?> CashEventFingerprint(Fixture fixture) => fixture.Scalar("""
        SELECT coalesce((SELECT group_concat(branch_id || request_id || result_json || bill_snapshot_json) FROM cash_sales),'')
            || ':' || coalesce((SELECT group_concat(audit_id || request_id || action || coalesce(previous_status,'') || current_status
                || previous_table_id || current_table_id || table_version || employee_id || device_id || occurred_at || coalesce(reason,'')) FROM table_session_audit),'')
            || ':' || coalesce((SELECT group_concat(branch_id || cancellation_request_id || details_json) FROM cancellation_confirmations),'')
            || ':' || (SELECT count(*) FROM table_session_audit) || ':' || (SELECT count(*) FROM cloud_sync_outbox)
            || ':' || (SELECT count(*) FROM simulated_cash_receipt_jobs) || ':' || (SELECT count(*) FROM command_receipts);
        """);

    [Theory]
    [InlineData("Session")]
    [InlineData("Cancellation")]
    public async Task Cash_history_events_require_complete_pc_identity_and_do_not_disclose_foreign_cash(string source)
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = await SavedCashHistory(fixture);
        var incomplete = LifecyclePrincipal();
        var identity = (ClaimsIdentity)incomplete.Identity!;
        identity.RemoveClaim(identity.Claims.Single(claim => claim.Value == "EMP-1"));
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), incomplete, new ClaimsPrincipal() })
        {
            var denied = await fixture.Call("GetCashHistoryEvents", routeValue: saved.Request, principal: principal, query: "?source=" + source);
            Assert.Equal(403, denied.Status); Assert.Equal("no-store", denied.CacheControl);
        }
        var foreign = await fixture.Call("GetCashHistoryEvents", routeValue: saved.Request,
            principal: LifecyclePrincipal(branch: "OTHER"), query: "?source=" + source);
        Assert.Equal(404, foreign.Status); Assert.Equal("CashHistoryNotFound", foreign.Body.GetProperty("error").GetString());
        var missing = await fixture.Call("GetCashHistoryEvents", routeValue: "MISSING-CASH", principal: LifecyclePrincipal());
        Assert.Equal(404, missing.Status);
    }

    [Theory]
    [InlineData("?source=")]
    [InlineData("?source=session")]
    [InlineData("?source=All")]
    [InlineData("?source=Session&source=Cancellation")]
    [InlineData("?Source=Session")]
    [InlineData("?source=Session&branchId=OTHER")]
    [InlineData("?source=Session&limit=0")]
    [InlineData("?limit=51")]
    [InlineData("?limit=1.0")]
    [InlineData("?limit=%2020")]
    [InlineData("?cursor=")]
    [InlineData("?cursor=not-a-cursor")]
    public async Task Cash_history_events_reject_ambiguous_noncanonical_queries_without_mutations(string query)
    {
        await using var fixture = await Fixture.CreateAsync(); var saved = await SavedCashHistory(fixture);
        var before = await CashEventFingerprint(fixture);
        var invalid = await fixture.Call("GetCashHistoryEvents", routeValue: saved.Request, principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, invalid.Status); Assert.Equal("no-store", invalid.CacheControl);
        Assert.Equal("InvalidCashHistoryEventQuery", invalid.Body.GetProperty("error").GetString());
        Assert.Equal(before, await CashEventFingerprint(fixture));
    }

    [Theory]
    [InlineData(" bad")]
    [InlineData("bad ")]
    [InlineData("bad\n")]
    public async Task Cash_history_events_reject_invalid_parent_identity(string request)
    {
        await using var fixture = await Fixture.CreateAsync();
        var invalid = await fixture.Call("GetCashHistoryEvents", routeValue: request, principal: LifecyclePrincipal());
        Assert.Equal(400, invalid.Status); Assert.Equal("InvalidCashHistoryIdentity", invalid.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Cash_history_session_events_read_original_capture_audit_with_scoped_pagination_and_no_writes()
    {
        await using var fixture = await Fixture.CreateAsync(); await CapturedItemHistory(fixture);
        var otherParent = await SavedCashHistory(fixture);
        var before = await CashEventFingerprint(fixture);
        var first = await fixture.Call("GetCashHistoryEvents", routeValue: "CASH-ITEMS", principal: LifecyclePrincipal(), query: "?limit=1");
        Assert.Equal(200, first.Status); Assert.Equal("no-store", first.CacheControl);
        Assert.Equal(1, first.Body.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("BRANCH-01", first.Body.GetProperty("branchId").GetString());
        Assert.Equal("CASH-ITEMS", first.Body.GetProperty("cashRequestId").GetString());
        Assert.Equal("SESSION-1", first.Body.GetProperty("tableSessionId").GetString());
        Assert.Equal("Session", first.Body.GetProperty("source").GetString());
        var last = Assert.Single(first.Body.GetProperty("events").EnumerateArray());
        Assert.Equal("Session", last.GetProperty("source").GetString());
        Assert.Equal("CompletePayment", last.GetProperty("session").GetProperty("action").GetString());
        Assert.Equal("Completed", last.GetProperty("session").GetProperty("currentStatus").GetString());
        Assert.Equal("CASH-ITEMS", last.GetProperty("requestId").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("cancellation").ValueKind);
        var cursor = first.Body.GetProperty("nextCursor").GetString(); Assert.NotNull(cursor);
        var second = await fixture.Call("GetCashHistoryEvents", routeValue: "CASH-ITEMS", principal: LifecyclePrincipal(),
            query: "?limit=1&cursor=" + Uri.EscapeDataString(cursor));
        Assert.Equal(200, second.Status);
        Assert.NotEqual(last.GetProperty("eventId").GetString(), Assert.Single(second.Body.GetProperty("events").EnumerateArray()).GetProperty("eventId").GetString());
        foreach (var query in new[] { "?source=Cancellation&limit=1&cursor=", "?source=Session&limit=2&cursor=" })
        {
            Assert.Equal(400, (await fixture.Call("GetCashHistoryEvents", routeValue: "CASH-ITEMS", principal: LifecyclePrincipal(),
                query: query + Uri.EscapeDataString(cursor))).Status);
        }
        Assert.Equal(400, (await fixture.Call("GetCashHistoryEvents", routeValue: otherParent.Request, principal: LifecyclePrincipal(),
            query: "?limit=1&cursor=" + Uri.EscapeDataString(cursor))).Status);
        Assert.Equal(before, await CashEventFingerprint(fixture));
        Assert.DoesNotContain("result_json", first.Body.GetRawText());
        Assert.DoesNotContain("bill_snapshot", first.Body.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cash_history_confirmed_cancellation_events_preserve_recorded_counts_not_live_request_state(bool partial)
    {
        await using var fixture = await Fixture.CreateAsync(cancellationEnabled: true);
        await CaptureCashAfterCancellation(fixture, partial);
        var before = await CashEventFingerprint(fixture);
        var first = await fixture.Call("GetCashHistoryEvents", routeValue: "CASH-EVENTS", principal: LifecyclePrincipal(), query: "?source=Cancellation");
        Assert.Equal(200, first.Status); Assert.Equal("no-store", first.CacheControl);
        var evidence = Assert.Single(first.Body.GetProperty("events").EnumerateArray());
        Assert.Equal("Cancellation", evidence.GetProperty("source").GetString());
        Assert.Equal("CANCEL-EVENTS", evidence.GetProperty("requestId").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("deviceId").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("session").ValueKind);
        var cancellation = evidence.GetProperty("cancellation");
        Assert.Equal(partial ? "PartiallyConfirmed" : "Confirmed", cancellation.GetProperty("outcome").GetString());
        var lines = cancellation.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(partial ? 2 : 1, lines.Length);
        Assert.All(lines, line => Assert.Equal(1, line.GetProperty("requestedQuantity").GetInt32()));
        Assert.Equal(1, lines.Sum(line => line.GetProperty("cancelled").GetProperty("notStarted").GetInt32()));
        Assert.Equal(partial ? 1 : 0, lines.Sum(line => line.GetProperty("notCancelledQuantity").GetInt64()));
        Assert.Equal(before, await CashEventFingerprint(fixture));
        await fixture.Execute("""
            UPDATE cancellation_requests SET status='PRIVATE-CHANGED';
            UPDATE cancellation_request_reasons SET reason='PRIVATE-CURRENT-REASON';
            UPDATE order_lines SET display_name='PRIVATE-CURRENT-NAME',unit_price='999';
            """);
        var reopened = await fixture.Call("GetCashHistoryEvents", routeValue: "CASH-EVENTS", principal: LifecyclePrincipal(), query: "?source=Cancellation");
        Assert.Equal(200, reopened.Status);
        Assert.Equal(first.Body.GetProperty("events").GetRawText(), reopened.Body.GetProperty("events").GetRawText());
        Assert.DoesNotContain("PRIVATE", reopened.Body.GetRawText());
        Assert.DoesNotContain("amount", reopened.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Session")]
    [InlineData("Cancellation")]
    public async Task Cash_history_events_empty_sources_are_successful_and_distinct_from_corruption(string source)
    {
        await using var fixture = await Fixture.CreateAsync(); var saved = await SavedCashHistory(fixture);
        var empty = await fixture.Call("GetCashHistoryEvents", routeValue: saved.Request, principal: LifecyclePrincipal(), query: "?source=" + source);
        Assert.Equal(200, empty.Status); Assert.Empty(empty.Body.GetProperty("events").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, empty.Body.GetProperty("nextCursor").ValueKind);
        await fixture.Execute("UPDATE cash_sales SET result_json='{}';");
        var unavailable = await fixture.Call("GetCashHistoryEvents", routeValue: saved.Request, principal: LifecyclePrincipal(), query: "?source=" + source);
        Assert.Equal(503, unavailable.Status); Assert.Equal("no-store", unavailable.CacheControl);
    }

    [Theory]
    [InlineData("Session")]
    [InlineData("Cancellation")]
    public async Task Cash_history_events_corrupt_selected_evidence_is_unavailable_without_disclosure_or_repair(string source)
    {
        await using var fixture = await Fixture.CreateAsync(); var saved = await SavedCashHistory(fixture);
        await fixture.Execute(source == "Session" ? $$"""
            INSERT INTO table_session_audit VALUES ('PRIVATE-AUDIT','BRANCH-01','OLD-OP','{{saved.Session}}',
                'OpenTable',NULL,'Ordering','A01','A01',1,'OLD-EMPLOYEE','OLD-DEVICE','PRIVATE-BAD-TIME',NULL);
            """ : $$"""
            INSERT INTO cancellation_requests VALUES ('BRANCH-01','PRIVATE-CANCEL','{{saved.Session}}','Confirmed');
            INSERT INTO cancellation_confirmations VALUES ('BRANCH-01','PRIVATE-CANCEL','OLD-EMPLOYEE','2026-11-24T10:00:00Z','[{}]');
            """);
        var before = await CashEventFingerprint(fixture);
        var response = await fixture.Call("GetCashHistoryEvents", routeValue: saved.Request,
            principal: LifecyclePrincipal(), query: "?source=" + source);
        Assert.Equal(503, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.True(response.Body.ValueKind == JsonValueKind.Undefined || !response.Body.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
        Assert.Equal(before, await CashEventFingerprint(fixture));
    }

    private static async Task CaptureCashAfterCancellation(Fixture fixture, bool partial)
    {
        Assert.Equal(200, (await fixture.Call("OpenBranchTable", Open("OPEN-EVENTS", "SESSION-1", "A01"))).Status);
        Assert.Equal(200, (await fixture.Call("SubmitBranchOrder", Submit("ORDER-EVENTS"))).Status);
        var first = await fixture.Scalar("SELECT order_line_id FROM order_lines ORDER BY line_sequence LIMIT 1;");
        var second = await fixture.Scalar("SELECT order_line_id FROM order_lines ORDER BY line_sequence DESC LIMIT 1;");
        var ids = partial ? new[] { first!, second! } : new[] { first! };
        var request = new
        {
            schemaVersion = 1,
            context = Context("CANCEL-EVENTS", "SESSION-1"),
            reason = "Original reason",
            lines = ids.Select(id => new { orderLineId = id, quantity = 1 }).ToArray()
        };
        Assert.Equal(200, (await fixture.Call("RequestBranchCancellation", request, principal: LifecyclePrincipal("Mobile"))).Status);
        Assert.Equal(200, (await fixture.Call("QueueBranchCancellationInspection", routeValue: "CANCEL-EVENTS", principal: LifecyclePrincipal())).Status);
        object Counts(bool confirm) => new
        {
            schemaVersion = 1,
            lines = ids.Select((id, index) =>
            new { orderLineId = id, notStarted = confirm && index > 0 ? 0 : 1, inProgress = 0, completed = 0, notApplicable = 0 }).ToArray()
        };
        Assert.Equal(200, (await fixture.Call("RecordBranchCancellationProduction", Counts(false), "CANCEL-EVENTS", LifecyclePrincipal())).Status);
        Assert.Equal(200, (await fixture.Call("ConfirmBranchCancellation", Counts(true), "CANCEL-EVENTS", LifecyclePrincipal())).Status);
        var current = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1");
        var payment = await fixture.Call("RequestBranchTablePayment", new
        {
            schemaVersion = 1,
            context = Context("PAY-EVENTS", "SESSION-1"),
            expectedVersion = current.Body.GetProperty("session").GetProperty("tableVersion").GetInt64()
        });
        Assert.Equal(200, payment.Status);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        Assert.True(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-EVENTS", "STORE-EVENTS",
            DateOnly.ParseExact(CashHistoryDay(), "yyyy-MM-dd", CultureInfo.InvariantCulture), 500, default));
        var completed = await fixture.Call("CompleteBranchCashSale", new
        {
            schemaVersion = 1,
            context = Context("CASH-EVENTS", "SESSION-1"),
            expectedVersion = payment.Body.GetProperty("result").GetProperty("tableVersion").GetInt64(),
            tenderedAmount = 300m
        }, principal: LifecyclePrincipal());
        Assert.Equal(200, completed.Status);
    }
}
