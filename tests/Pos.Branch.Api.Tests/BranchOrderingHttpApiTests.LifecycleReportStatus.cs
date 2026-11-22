using System.Security.Claims;
using System.Text.Json;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Theory]
    [InlineData("Queued")]
    [InlineData("InFlight")]
    [InlineData("LeaseExpired")]
    [InlineData("RetryScheduled")]
    [InlineData("Delivered")]
    public async Task Lifecycle_report_status_is_current_on_list_and_detail_without_exposing_private_delivery_fields(string state)
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var now = DateTimeOffset.UtcNow;
        var parent = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("BRANCH-01", "PRIVATE-PARENT-WORKER", now, TimeSpan.FromMinutes(1)));
        Assert.True(await store.ConfirmClosingReportAsync(parent, now));
        ClosingLifecycleLease? lease = null;
        if (state != "Queued")
        {
            lease = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("BRANCH-01", "PRIVATE-CONTEXT-WORKER", now, TimeSpan.FromMinutes(1)));
            if (state == "LeaseExpired")
            {
                await fixture.Execute($"UPDATE closing_report_context_outbox SET lease_expires_at='2026-01-01T00:00:00Z' WHERE request_id='{close.RequestId}';");
            }
            if (state == "RetryScheduled") { Assert.True(await store.RetryClosingLifecycleAsync(lease, now, TimeSpan.FromHours(1), ClosingLifecycleFailure.ParentUnavailable)); }
            if (state == "Delivered") { Assert.True(await store.ConfirmClosingLifecycleAsync(lease, now)); }
        }
        var frozen = await fixture.Scalar($"SELECT context_json FROM closing_report_lifecycle_snapshots WHERE request_id='{close.RequestId}';");
        var before = await ReportStatusFingerprint(fixture);
        var filter = state == "Delivered" ? "Delivered" : "Pending";
        var response = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?status=Delivered&lifecycleStatus=" + filter);
        Assert.Equal(200, response.Status); Assert.Equal("no-store", response.CacheControl);
        var item = Assert.Single(response.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(close.RequestId, item.GetProperty("requestId").GetString());
        Assert.Equal("Delivered", item.GetProperty("deliveryState").GetString());
        var context = item.GetProperty("lifecycleDelivery");
        Assert.Equal(new[] { "attemptCount", "availableAt", "contextId", "deliveredAt", "deliveryState", "lastFailure", "leaseExpiresAt" },
            context.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.Equal("lifecycle:" + parent.ReportId, context.GetProperty("contextId").GetString());
        Assert.Equal(state, context.GetProperty("deliveryState").GetString());
        Assert.Equal(state == "Queued" ? 0 : 1, context.GetProperty("attemptCount").GetInt32());
        Assert.Equal(state == "Delivered" ? JsonValueKind.String : JsonValueKind.Null, context.GetProperty("deliveredAt").ValueKind);
        Assert.Equal(state == "RetryScheduled" ? "ParentUnavailable" : null, context.GetProperty("lastFailure").GetString());
        var detail = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status); Assert.Equal("no-store", detail.CacheControl);
        Assert.Equal(context.GetRawText(), detail.Body.GetProperty("lifecycleDelivery").GetRawText());
        Assert.Equal(context.GetRawText(), detail.Body.GetProperty("report").GetProperty("lifecycleDelivery").GetRawText());
        Assert.DoesNotContain("PRIVATE-", response.Body.GetRawText());
        Assert.DoesNotContain("leaseId", response.Body.GetRawText());
        Assert.DoesNotContain("payloadJson", response.Body.GetRawText());
        if (lease is not null) { Assert.DoesNotContain(lease.LeaseId, response.Body.GetRawText()); }
        Assert.Equal(before, await ReportStatusFingerprint(fixture));
        Assert.Equal(frozen, await fixture.Scalar($"SELECT context_json FROM closing_report_lifecycle_snapshots WHERE request_id='{close.RequestId}';"));
    }

    [Fact]
    public async Task Lifecycle_report_status_legacy_absence_is_explicit_null_and_not_inferred_from_parent_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var legacy = await SavedReport(fixture);
        await fixture.Execute($"DELETE FROM closing_report_context_outbox WHERE request_id='{legacy.RequestId}'; DELETE FROM closing_report_lifecycle_snapshots WHERE request_id='{legacy.RequestId}';");
        var store = new SqliteTableSessionCommandStore(fixture.Path); var now = DateTimeOffset.UtcNow;
        var parent = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("BRANCH-01", "parent", now, TimeSpan.FromMinutes(1)));
        Assert.True(await store.ConfirmClosingReportAsync(parent, now));
        var before = await ReportStatusFingerprint(fixture);
        var response = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?status=Delivered&lifecycleStatus=NotRecorded");
        Assert.Equal(200, response.Status);
        var row = Assert.Single(response.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("Delivered", row.GetProperty("deliveryState").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("lifecycleDelivery").ValueKind);
        var detail = await fixture.Call("GetClosingReportDetail", routeValue: legacy.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("lifecycleDelivery").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("report").GetProperty("lifecycleDelivery").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("lifecycle").ValueKind);
        foreach (var status in new[] { "Pending", "Delivered" })
        {
            Assert.Empty((await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=" + status)).Body.GetProperty("items").EnumerateArray());
        }
        Assert.Equal(before, await ReportStatusFingerprint(fixture));
    }

    [Fact]
    public async Task Lifecycle_report_status_confirmation_refresh_moves_membership_without_changing_saved_snapshot_or_parent()
    {
        await using var fixture = await Fixture.CreateAsync(); var close = await SavedReport(fixture);
        var store = new SqliteTableSessionCommandStore(fixture.Path); var now = DateTimeOffset.UtcNow;
        var parent = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("BRANCH-01", "parent", now, TimeSpan.FromMinutes(1)));
        Assert.True(await store.ConfirmClosingReportAsync(parent, now));
        var pending = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=Pending");
        var before = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal("Queued", Assert.Single(pending.Body.GetProperty("items").EnumerateArray()).GetProperty("lifecycleDelivery").GetProperty("deliveryState").GetString());
        var lease = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("BRANCH-01", "context", now, TimeSpan.FromMinutes(1)));
        Assert.True(await store.ConfirmClosingLifecycleAsync(lease, now));
        var reopened = new SqliteTableSessionCommandStore(fixture.Path);
        var result = await reopened.ReadClosingReportPageAsync("BRANCH-01", DateTimeOffset.UtcNow, new(LifecycleStatus: "Delivered"));
        Assert.Equal("Delivered", Assert.Single(result.Items).LifecycleDelivery!.DeliveryState);
        Assert.Empty((await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=Pending")).Body.GetProperty("items").EnumerateArray());
        var delivered = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=Delivered");
        Assert.Equal("Delivered", Assert.Single(delivered.Body.GetProperty("items").EnumerateArray()).GetProperty("lifecycleDelivery").GetProperty("deliveryState").GetString());
        var after = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(before.Body.GetProperty("snapshot").GetRawText(), after.Body.GetProperty("snapshot").GetRawText());
        Assert.Equal(before.Body.GetProperty("lifecycle").GetRawText(), after.Body.GetProperty("lifecycle").GetRawText());
        Assert.Equal(before.Body.GetProperty("report").GetProperty("deliveredAt").GetRawText(), after.Body.GetProperty("report").GetProperty("deliveredAt").GetRawText());
        Assert.Equal(1, after.Body.GetProperty("report").GetProperty("attemptCount").GetInt32());
    }

    [Fact]
    public async Task Lifecycle_report_status_remains_pc_authorized_branch_scoped_and_read_only()
    {
        await using var fixture = await Fixture.CreateAsync();
        var local = await SavedReport(fixture); var foreign = await SavedReport(fixture, "OTHER");
        var before = await ReportStatusFingerprint(fixture);
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), new ClaimsPrincipal() })
        {
            var denied = await fixture.Call("GetClosingReports", principal: principal);
            Assert.Equal(403, denied.Status); Assert.Equal("no-store", denied.CacheControl);
        }
        var localPage = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal());
        var foreignPage = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(local.RequestId, Assert.Single(localPage.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        Assert.Equal(foreign.RequestId, Assert.Single(foreignPage.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        Assert.DoesNotContain(foreign.RequestId, localPage.Body.GetRawText());
        Assert.DoesNotContain(local.RequestId, foreignPage.Body.GetRawText());
        Assert.Equal("no-store", localPage.CacheControl); Assert.Equal("no-store", foreignPage.CacheControl);
        Assert.Equal(before, await ReportStatusFingerprint(fixture));
    }

    private static async Task<string> ReportStatusFingerprint(Fixture fixture)
    {
        var values = new List<string?> { await LifecycleFilterFingerprint(fixture) };
        foreach (var table in new[] { "closing_report_outbox", "closing_report_context_outbox" })
        {
            values.Add(await fixture.Scalar($"SELECT group_concat(available_at || ':' || coalesce(lease_expires_at,'') || ':' || coalesce(lease_owner,'')) FROM {table};"));
        }
        return JsonSerializer.Serialize(values);
    }
}
