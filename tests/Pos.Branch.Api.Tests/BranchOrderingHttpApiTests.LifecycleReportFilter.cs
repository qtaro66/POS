using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Theory]
    [InlineData("?lifecycleStatus=")]
    [InlineData("?lifecycleStatus=pending")]
    [InlineData("?lifecycleStatus=Unknown")]
    [InlineData("?lifecycleStatus=%20All")]
    [InlineData("?lifecycleStatus=Pending&lifecycleStatus=Delivered")]
    [InlineData("?LifecycleStatus=All")]
    [InlineData("?lifecycleStatus=All&branchId=OTHER")]
    public async Task Lifecycle_report_filter_rejects_noncanonical_or_ambiguous_queries(string query)
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, response.Status);
        Assert.Equal("InvalidClosingReportQuery", response.Body.GetProperty("error").GetString());
        Assert.Equal("no-store", response.CacheControl);
    }

    [Fact]
    public async Task Lifecycle_report_filter_is_independent_of_parent_confirmation_and_scoped_to_store_shift_day()
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture);
        await SavedReport(fixture);
        await SavedReport(fixture, "OTHER");
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var now = DateTimeOffset.UtcNow;
        var parent = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("BRANCH-01", "parent", now, TimeSpan.FromMinutes(1)));
        Assert.Equal(close.RequestId, parent.RequestId);
        Assert.True(await store.ConfirmClosingReportAsync(parent, now));
        var scope = $"&storeSessionId={close.StoreSessionId}&shiftId={close.ShiftId}&businessDate={close.Context.BranchLocalDate:yyyy-MM-dd}";
        var query = "?status=Delivered&lifecycleStatus=Pending" + scope;
        var pending = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(200, pending.Status);
        Assert.Equal("no-store", pending.CacheControl);
        var row = Assert.Single(pending.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(close.RequestId, row.GetProperty("requestId").GetString());
        Assert.Equal("Delivered", row.GetProperty("deliveryState").GetString());
        Assert.Empty((await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query.Replace("status=Delivered", "status=Pending"))).Body.GetProperty("items").EnumerateArray());
        Assert.Empty((await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(branch: "OTHER"), query: query)).Body.GetProperty("items").EnumerateArray());
        var context = Assert.IsType<ClosingLifecycleLease>(await store.ClaimClosingLifecycleAsync("BRANCH-01", "context", now, TimeSpan.FromMinutes(1)));
        Assert.True(await store.ConfirmClosingLifecycleAsync(context, now));
        Assert.Empty((await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query)).Body.GetProperty("items").EnumerateArray());
        var confirmed = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: query.Replace("lifecycleStatus=Pending", "lifecycleStatus=Delivered"));
        Assert.Equal(close.RequestId, Assert.Single(confirmed.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task Lifecycle_report_filter_distinguishes_not_recorded_history_from_pending_and_never_reconstructs_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        var legacy = await SavedReport(fixture);
        var captured = await SavedReport(fixture);
        await fixture.Execute($"DELETE FROM closing_report_context_outbox WHERE request_id='{legacy.RequestId}'; DELETE FROM closing_report_lifecycle_snapshots WHERE request_id='{legacy.RequestId}';");
        var before = await LifecycleFilterFingerprint(fixture);
        var historical = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=NotRecorded");
        Assert.Equal(200, historical.Status);
        Assert.Equal(legacy.RequestId, Assert.Single(historical.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        var pending = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=Pending");
        Assert.Equal(captured.RequestId, Assert.Single(pending.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        Assert.Equal(before, await LifecycleFilterFingerprint(fixture));
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Pending")]
    [InlineData("Delivered")]
    [InlineData("NotRecorded")]
    public async Task Lifecycle_report_filter_keeps_read_authorization_and_no_store_for_every_status(string status)
    {
        await using var fixture = await Fixture.CreateAsync();
        await SavedReport(fixture);
        var before = await LifecycleFilterFingerprint(fixture);
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), new System.Security.Claims.ClaimsPrincipal() })
        {
            var response = await fixture.Call("GetClosingReports", principal: principal, query: "?lifecycleStatus=" + status);
            Assert.Equal(403, response.Status); Assert.Equal("no-store", response.CacheControl);
        }
        var valid = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=" + status);
        Assert.Equal(200, valid.Status); Assert.Equal("no-store", valid.CacheControl);
        Assert.Equal(before, await LifecycleFilterFingerprint(fixture));
    }

    [Fact]
    public async Task Lifecycle_report_filter_cursor_binds_both_statuses_and_rejects_old_cursor_until_refresh()
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = await SavedReport(fixture); var newer = await SavedReport(fixture);
        var first = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=Pending&limit=1");
        Assert.Equal(newer.RequestId, Assert.Single(first.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        var raw = first.Body.GetProperty("nextCursor").GetString()!;
        var cursor = Uri.EscapeDataString(raw);
        var appended = await SavedReport(fixture);
        var next = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=Pending&limit=1&cursor=" + cursor);
        Assert.Equal(200, next.Status);
        Assert.Equal(older.RequestId, Assert.Single(next.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        foreach (var prefix in new[] { "?lifecycleStatus=All&limit=1", "?lifecycleStatus=NotRecorded&limit=1", "?lifecycleStatus=Pending&status=Delivered&limit=1" })
        {
            Assert.Equal(400, (await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: prefix + "&cursor=" + cursor)).Status);
        }
        var normalized = raw.Replace('-', '+').Replace('_', '/');
        var document = JsonNode.Parse(Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')))!.AsObject();
        document["Version"] = 1; document.Remove("LifecycleStatus");
        var old = Convert.ToBase64String(Encoding.UTF8.GetBytes(document.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(400, (await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?limit=1&cursor=" + Uri.EscapeDataString(old))).Status);
        var refreshed = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=Pending&limit=1");
        Assert.Equal(appended.RequestId, Assert.Single(refreshed.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
    }

    [Theory]
    [InlineData("All")]
    [InlineData("NotRecorded")]
    [InlineData("Pending")]
    public async Task Lifecycle_report_filter_missing_recorded_job_is_unavailable_not_missing_history(string status)
    {
        await using var fixture = await Fixture.CreateAsync();
        var close = await SavedReport(fixture);
        await fixture.Execute($"DELETE FROM closing_report_context_outbox WHERE request_id='{close.RequestId}';");
        var before = await LifecycleFilterFingerprint(fixture);
        var response = await fixture.Call("GetClosingReports", principal: LifecyclePrincipal(), query: "?lifecycleStatus=" + status);
        Assert.Equal(503, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(before, await LifecycleFilterFingerprint(fixture));
    }

    private static async Task<string> LifecycleFilterFingerprint(Fixture fixture)
    {
        var values = new List<string?>();
        foreach (var table in new[] { "closing_report_outbox", "closing_report_lifecycle_snapshots", "closing_report_context_outbox" })
        {
            var expression = table == "closing_report_lifecycle_snapshots" ? "context_json" : "payload_json || ':' || attempt_count || ':' || coalesce(delivered_at,'') || ':' || coalesce(lease_id,'') || ':' || coalesce(last_failure,'')";
            values.Add(await fixture.Scalar($"SELECT group_concat({expression}) FROM {table};"));
        }
        return JsonSerializer.Serialize(values);
    }
}
