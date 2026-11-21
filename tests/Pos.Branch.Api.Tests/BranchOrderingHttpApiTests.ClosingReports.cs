using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private static async Task<StoreLifecycleCommand> SavedReport(Fixture fixture, string branch = "BRANCH-01",
        StoreLifecycleAction operation = StoreLifecycleAction.CloseStore, decimal? actual = 490, bool withSale = false)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
        var open = new StoreLifecycleCommand(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            StoreLifecycleAction.OpenStore, 500, new("EMP-CLOSE", "DEV-CLOSE", now, day));
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        await store.ExecuteStoreLifecycleAsync(branch, open);
        if (withSale)
        {
            var saleId = Guid.NewGuid().ToString();
            var sale = JsonSerializer.Serialize(new CashSaleResult(saleId, "EMP-SALE", "DEV-SALE", 1, open.ShiftId,
                open.StoreSessionId, day.ToString("yyyy-MM-dd"), 120, 110, 10, "SYNTHETIC-RECEIPT", "QueuedForSimulation"));
            await fixture.Execute($$"""
                INSERT INTO table_sessions VALUES ('{{branch}}','{{saleId}}','SYNTHETIC-TABLE','Completed',1);
                INSERT INTO cash_sales VALUES ('{{branch}}','{{saleId}}','{{saleId}}','{{sale}}',
                '{"itemGrossAmount":120,"lineDiscountAmount":10,"billDiscount":{"appliedAmount":5},"serviceChargeAmount":5.9,"totalBeforeRounding":110.9,"roundingAdjustment":-0.9,"netTotal":110,"vatInclusiveAmount":7.26,"amountBeforeVat":103.64}');
                """);
        }
        var close = open with { RequestId = Guid.NewGuid().ToString(), Action = operation, CashAmount = actual };
        await store.ExecuteStoreLifecycleAsync(branch, close);
        return close;
    }

    [Theory]
    [InlineData("GetClosingReports")]
    [InlineData("GetClosingReportDetail")]
    [InlineData("GetClosingReportDelivery")]
    public async Task Closing_report_workspace_requires_complete_pc_identity(string endpoint)
    {
        await using var fixture = await Fixture.CreateAsync();
        var incomplete = LifecyclePrincipal();
        var identity = (ClaimsIdentity)incomplete.Identity!;
        identity.RemoveClaim(identity.Claims.Single(claim => claim.Value == "EMP-1"));
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), incomplete, new ClaimsPrincipal() })
        {
            var result = await fixture.Call(endpoint, routeValue: Guid.NewGuid().ToString(), principal: principal);
            Assert.Equal(403, result.Status);
            Assert.Equal("no-store", result.CacheControl);
        }
    }

    [Fact]
    public async Task Closing_report_workspace_is_branch_scoped_and_reads_do_not_change_durable_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture);
        await SavedReport(fixture, "OTHER");
        var before = await fixture.Scalar("SELECT group_concat(payload_json || ':' || attempt_count || ':' || coalesce(delivered_at,'')) FROM closing_report_outbox;");
        var result = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal());
        Assert.Equal(200, result.Status);
        Assert.Equal("no-store", result.CacheControl);
        Assert.Equal("BRANCH-01", result.Body.GetProperty("branchId").GetString());
        var item = Assert.Single(result.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(close.RequestId, item.GetProperty("requestId").GetString());
        Assert.Equal("Queued", item.GetProperty("deliveryState").GetString());
        var detail = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status);
        Assert.Equal("no-store", detail.CacheControl);
        Assert.Equal(close.RequestId, detail.Body.GetProperty("snapshot").GetProperty("requestId").GetString());
        var foreign = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(404, foreign.Status);
        Assert.Equal("ClosingReportNotFound", foreign.Body.GetProperty("error").GetString());
        Assert.Equal(before, await fixture.Scalar("SELECT group_concat(payload_json || ':' || attempt_count || ':' || coalesce(delivered_at,'')) FROM closing_report_outbox;"));
    }

    [Theory]
    [InlineData("?status=pending")]
    [InlineData("?status=All&status=Pending")]
    [InlineData("?status=")]
    [InlineData("?unknown=secret")]
    [InlineData("?storeSessionId=bad")]
    [InlineData("?shiftId=bad")]
    [InlineData("?businessDate=2026-02-30")]
    [InlineData("?businessDate=2026-1-1")]
    [InlineData("?limit=0")]
    [InlineData("?limit=51")]
    [InlineData("?limit=-1")]
    [InlineData("?limit=1.5")]
    [InlineData("?cursor=not-valid-json")]
    public async Task Closing_report_workspace_rejects_invalid_or_ambiguous_queries(string query)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, result.Status);
        Assert.Equal("InvalidClosingReportQuery", result.Body.GetProperty("error").GetString());
        Assert.Equal("no-store", result.CacheControl);
    }

    [Fact]
    public async Task Closing_report_workspace_filters_store_shift_day_and_confirmed_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture);
        await SavedReport(fixture);
        var query = $"?status=Pending&storeSessionId={close.StoreSessionId}&shiftId={close.ShiftId}&businessDate={close.Context.BranchLocalDate:yyyy-MM-dd}";
        var pending = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(200, pending.Status);
        Assert.Equal(close.RequestId, Assert.Single(pending.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("BRANCH-01", "WORKER-SECRET", now, TimeSpan.FromMinutes(1)));
        Assert.Equal(close.RequestId, lease.RequestId);
        Assert.True(await store.ConfirmClosingReportAsync(lease, now));
        var noLongerPending = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query);
        Assert.Empty(noLongerPending.Body.GetProperty("items").EnumerateArray());
        var confirmed = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query.Replace("Pending", "Delivered"));
        Assert.Equal("Delivered", Assert.Single(confirmed.Body.GetProperty("items").EnumerateArray()).GetProperty("deliveryState").GetString());
    }

    [Fact]
    public async Task Closing_report_workspace_pages_bind_scope_and_exclude_new_appends()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await SavedReport(fixture);
        var second = await SavedReport(fixture);
        var third = await SavedReport(fixture);
        var page = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?limit=1");
        Assert.Equal(third.RequestId, Assert.Single(page.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        var cursor = Uri.EscapeDataString(page.Body.GetProperty("nextCursor").GetString()!);
        await SavedReport(fixture);
        var next = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?limit=1&cursor=" + cursor);
        Assert.Equal(second.RequestId, Assert.Single(next.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        var lastCursor = Uri.EscapeDataString(next.Body.GetProperty("nextCursor").GetString()!);
        var last = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?limit=1&cursor=" + lastCursor);
        Assert.Equal(first.RequestId, Assert.Single(last.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        Assert.Equal(JsonValueKind.Null, last.Body.GetProperty("nextCursor").ValueKind);
        Assert.Equal(400, (await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(branch: "OTHER"), query: "?limit=1&cursor=" + cursor)).Status);
        Assert.Equal(400, (await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?limit=1&status=Pending&cursor=" + cursor)).Status);
        Assert.Equal(400, (await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?limit=2&cursor=" + cursor)).Status);
    }

    [Fact]
    public async Task Closing_report_workspace_projects_frozen_counts_and_hides_internal_worker_or_failure_data()
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture);
        var before = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        var saved = before.Body.GetProperty("snapshot").GetRawText();
        Assert.Equal(500, before.Body.GetProperty("snapshot").GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(-10, before.Body.GetProperty("snapshot").GetProperty("closing").GetProperty("cashDifference").GetDecimal());
        await fixture.Execute("UPDATE master_data_packages SET lifecycle_status='Previous'; INSERT INTO table_sessions VALUES ('BRANCH-01','LATER','A99','Ordering',1);");
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("BRANCH-01", "WORKER-PRIVATE", now, TimeSpan.FromMinutes(1)));
        var active = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal("InFlight", active.Body.GetProperty("report").GetProperty("deliveryState").GetString());
        Assert.DoesNotContain("WORKER-PRIVATE", active.Body.GetRawText());
        Assert.DoesNotContain(lease.LeaseId, active.Body.GetRawText());
        Assert.DoesNotContain("leaseOwner", active.Body.GetRawText());
        Assert.DoesNotContain("payloadJson", active.Body.GetRawText());
        Assert.Equal(saved, active.Body.GetProperty("snapshot").GetRawText());
        Assert.True(await store.RetryClosingReportAsync(lease, now, TimeSpan.FromMinutes(2), ClosingReportDeliveryFailure.AcknowledgmentUnavailable));
        var retry = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal("RetryScheduled", retry.Body.GetProperty("report").GetProperty("deliveryState").GetString());
        Assert.Equal("AcknowledgmentUnavailable", retry.Body.GetProperty("report").GetProperty("lastFailure").GetString());
        await fixture.Execute("UPDATE closing_report_outbox SET last_failure='RAW-DATABASE-CONNECTION-SECRET';");
        var sanitized = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(JsonValueKind.Null, sanitized.Body.GetProperty("report").GetProperty("lastFailure").ValueKind);
        Assert.DoesNotContain("RAW-DATABASE", sanitized.Body.GetRawText());
        Assert.Equal(saved, sanitized.Body.GetProperty("snapshot").GetRawText());
    }

    [Fact]
    public async Task Closing_report_workspace_saved_financials_survive_live_sale_and_master_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture, withSale: true);
        var first = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, first.Status);
        var snapshot = first.Body.GetProperty("snapshot");
        Assert.Equal(610, snapshot.GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
        var financials = snapshot.GetProperty("cashSummary").GetProperty("store").GetProperty("financials");
        Assert.Equal(120, financials.GetProperty("itemGrossAmount").GetDecimal());
        Assert.Equal(5, financials.GetProperty("billDiscountAmount").GetDecimal());
        Assert.Equal(-0.9m, financials.GetProperty("roundingAdjustment").GetDecimal());
        Assert.Equal(7.26m, financials.GetProperty("vatInclusiveAmount").GetDecimal());
        Assert.Equal(110, financials.GetProperty("amountDue").GetDecimal());
        await fixture.Execute("UPDATE cash_sales SET bill_snapshot_json='{}'; UPDATE master_data_packages SET lifecycle_status='Previous';");
        var later = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(snapshot.GetRawText(), later.Body.GetProperty("snapshot").GetRawText());
    }

    [Fact]
    public async Task Closing_report_workspace_legacy_summary_is_unavailable_instead_of_reconstructed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture, withSale: true);
        await fixture.Execute("UPDATE closing_report_outbox SET payload_json=json_set(payload_json,'$.cashSummary',NULL);");
        var detail = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status);
        Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("snapshot").GetProperty("cashSummary").ValueKind);
        Assert.Equal(610, detail.Body.GetProperty("snapshot").GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
    }

    [Theory]
    [InlineData("GetClosingReports")]
    [InlineData("GetClosingReportDetail")]
    public async Task Closing_report_workspace_missing_database_returns_unavailable(string endpoint)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Execute("DROP TABLE closing_report_outbox;");
        var response = await fixture.Call(endpoint, routeValue: Guid.NewGuid().ToString(), principal: LifecyclePrincipal());
        Assert.Equal(503, response.Status);
        Assert.Equal("no-store", response.CacheControl);
    }

    [Fact]
    public async Task Closing_report_workspace_handles_missing_or_invalid_detail_and_uncounted_shift()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(400, (await fixture.Call("GetClosingReportDetail", routeValue: "bad", principal: LifecyclePrincipal())).Status);
        var missing = await fixture.Call("GetClosingReportDetail", routeValue: Guid.NewGuid().ToString(), principal: LifecyclePrincipal());
        Assert.Equal(404, missing.Status);
        Assert.Equal("ClosingReportNotFound", missing.Body.GetProperty("error").GetString());
        var close = await SavedReport(fixture, operation: StoreLifecycleAction.CloseShift, actual: null);
        var detail = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status);
        Assert.Equal("NotReconciled", detail.Body.GetProperty("snapshot").GetProperty("closing").GetProperty("reconciliationStatus").GetString());
        Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("snapshot").GetProperty("closing").GetProperty("actualCashAmount").ValueKind);
    }

    [Theory]
    [InlineData(false, "BRANCH-01", false)]
    [InlineData(true, "BRANCH-01", true)]
    [InlineData(true, "OTHER", false)]
    public async Task Closing_report_workspace_delivery_projects_only_scoped_enabled_state(bool enabled, string branch, bool expected)
    {
        var context = new DefaultHttpContext();
        var runtime = new BranchOrderingRuntime("unused-not-opened.db", TimeProvider.System);
        var result = StoreLifecycleEndpoints.ClosingReportDeliveryAsync(LifecyclePrincipal(), runtime, context,
            Options.Create(new ClosingReportDeliveryOptions { Enabled = enabled, BranchId = branch, CentralOrigin = "https://sensitive-origin.example" }));
        var value = Assert.IsType<Ok<StoreLifecycleEndpoints.ClosingReportDeliveryStatus>>(result).Value!;
        Assert.Equal("BRANCH-01", value.BranchId);
        Assert.Equal(expected, value.DeliveryEnabled);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(3, JsonSerializer.SerializeToElement(value).EnumerateObject().Count());
    }
}
