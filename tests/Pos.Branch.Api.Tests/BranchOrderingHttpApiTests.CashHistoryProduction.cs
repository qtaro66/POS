using System.Security.Claims;
using System.Text.Json;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private const string ProductionList = "GetCashHistoryProductionOrders";
    private const string ProductionDetail = "GetCashHistoryProductionOrderDetail";

    private static async Task<string?> ProductionFingerprint(Fixture fixture) =>
        await CashEventFingerprint(fixture) + ":" + await fixture.Scalar("""
            SELECT (SELECT count(*) FROM print_jobs) || ':' ||
                coalesce((SELECT group_concat(snapshot_id || content_json || content_sha256) FROM production_order_snapshots),'');
            """);

    [Fact]
    public async Task Cash_history_production_reads_original_submission_including_cancelled_lines_without_writes()
    {
        await using var fixture = await Fixture.CreateAsync(cancellationEnabled: true);
        await CaptureCashAfterCancellation(fixture, false);
        var originalLine = (await fixture.Scalar("SELECT order_line_id FROM order_lines ORDER BY line_sequence LIMIT 1;"))!;
        var bill = await fixture.Call("GetCashHistoryDetail", routeValue: "CASH-EVENTS", principal: LifecyclePrincipal());
        Assert.Equal(200, bill.Status);
        Assert.DoesNotContain(bill.Body.GetProperty("bill").GetProperty("items").EnumerateArray(),
            line => line.GetProperty("orderLineId").GetString() == originalLine);
        var before = await ProductionFingerprint(fixture);
        var list = await fixture.Call(ProductionList, routeValue: "CASH-EVENTS", principal: LifecyclePrincipal(),
            query: "?orderLineId=" + Uri.EscapeDataString(originalLine));
        Assert.Equal(200, list.Status); Assert.Equal("no-store", list.CacheControl);
        Assert.Equal("CASH-EVENTS", list.Body.GetProperty("cashRequestId").GetString());
        Assert.Equal("SESSION-1", list.Body.GetProperty("tableSessionId").GetString());
        var item = Assert.Single(list.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("ORDER-EVENTS", item.GetProperty("submittedRequestId").GetString());
        Assert.Equal("KITCHEN-1", item.GetProperty("productionStationId").GetString());
        Assert.Contains(originalLine, item.GetProperty("orderLineIds").EnumerateArray().Select(value => value.GetString()));
        var snapshot = item.GetProperty("snapshotId").GetString()!;
        var detail = await fixture.Call(ProductionDetail, routeValue: "CASH-EVENTS", snapshotId: snapshot, principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status); Assert.Equal("no-store", detail.CacheControl);
        var lines = detail.Body.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.GetProperty("orderLineId").GetString() == originalLine);
        Assert.All(lines, line =>
        {
            Assert.Equal("Test item", line.GetProperty("displayName").GetString());
            Assert.Equal(1, line.GetProperty("quantity").GetInt32());
            Assert.Equal("test", line.GetProperty("comment").GetString());
            Assert.Equal("Test option", Assert.Single(line.GetProperty("options").EnumerateArray()).GetProperty("displayName").GetString());
        });
        Assert.Equal(new[] { "DineIn", "Takeaway" }, lines.Select(line => line.GetProperty("fulfillmentType").GetString()).ToArray());
        Assert.Equal(before, await ProductionFingerprint(fixture));
        await fixture.Execute("UPDATE order_lines SET display_name='PRIVATE-LIVE',original_quantity=9,unit_price='999'; UPDATE master_data_sellable_items SET display_name='PRIVATE-MASTER';");
        var reread = await fixture.Call(ProductionDetail, routeValue: "CASH-EVENTS", snapshotId: snapshot, principal: LifecyclePrincipal());
        Assert.Equal(200, reread.Status);
        Assert.Equal(detail.Body.GetProperty("lines").GetRawText(), reread.Body.GetProperty("lines").GetRawText());
        Assert.DoesNotContain("PRIVATE", reread.Body.GetRawText());
        Assert.DoesNotContain("contentJson", reread.Body.GetRawText());
        Assert.DoesNotContain("contentSha256", reread.Body.GetRawText());
    }

    [Theory]
    [InlineData("?orderLineId=")]
    [InlineData("?orderLineId=%20LINE")]
    [InlineData("?orderBatchId=B&orderBatchId=C")]
    [InlineData("?productionStationId=S&branchId=OTHER")]
    [InlineData("?limit=0")]
    [InlineData("?limit=51")]
    [InlineData("?limit=1.0")]
    [InlineData("?Limit=1")]
    [InlineData("?cursor=not-a-cursor")]
    public async Task Cash_history_production_rejects_ambiguous_queries(string query)
    {
        await using var fixture = await Fixture.CreateAsync(); var saved = await SavedCashHistory(fixture);
        var before = await ProductionFingerprint(fixture);
        var response = await fixture.Call(ProductionList, routeValue: saved.Request, principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal("InvalidCashHistoryProductionQuery", response.Body.GetProperty("error").GetString());
        Assert.Equal(before, await ProductionFingerprint(fixture));
    }

    [Theory]
    [InlineData(ProductionList)]
    [InlineData(ProductionDetail)]
    public async Task Cash_history_production_requires_complete_pc_identity_and_scopes_parent(string endpoint)
    {
        await using var fixture = await Fixture.CreateAsync(); await CapturedItemHistory(fixture);
        var snapshot = (await fixture.Scalar("SELECT snapshot_id FROM production_order_snapshots LIMIT 1;"))!;
        var incomplete = LifecyclePrincipal(); var claims = (ClaimsIdentity)incomplete.Identity!;
        claims.RemoveClaim(claims.Claims.Single(claim => claim.Value == "EMP-1"));
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), incomplete, new ClaimsPrincipal() })
        {
            var denied = await fixture.Call(endpoint, routeValue: "CASH-ITEMS", snapshotId: snapshot, principal: principal);
            Assert.Equal(403, denied.Status); Assert.Equal("no-store", denied.CacheControl);
        }
        var foreign = await fixture.Call(endpoint, routeValue: "CASH-ITEMS", snapshotId: snapshot, principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(404, foreign.Status); Assert.Equal("CashHistoryNotFound", foreign.Body.GetProperty("error").GetString());
        var missing = await fixture.Call(endpoint, routeValue: "MISSING", snapshotId: snapshot, principal: LifecyclePrincipal());
        Assert.Equal(404, missing.Status);
    }

    [Fact]
    public async Task Cash_history_production_snapshot_lookup_is_scoped_to_cash_session()
    {
        await using var fixture = await Fixture.CreateAsync(); await CapturedItemHistory(fixture);
        var other = await SavedCashHistory(fixture);
        var snapshot = (await fixture.Scalar("SELECT snapshot_id FROM production_order_snapshots LIMIT 1;"))!;
        var response = await fixture.Call(ProductionDetail, routeValue: other.Request, snapshotId: snapshot, principal: LifecyclePrincipal());
        Assert.Equal(404, response.Status); Assert.Equal("CashHistoryProductionSnapshotNotFound", response.Body.GetProperty("error").GetString());
        var empty = await fixture.Call(ProductionList, routeValue: other.Request, principal: LifecyclePrincipal());
        Assert.Equal(200, empty.Status); Assert.Empty(empty.Body.GetProperty("items").EnumerateArray());
        var filtered = await fixture.Call(ProductionList, routeValue: "CASH-ITEMS", principal: LifecyclePrincipal(), query: "?productionStationId=OTHER");
        Assert.Equal(200, filtered.Status); Assert.Empty(filtered.Body.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("content_sha256=printf('%064d',0)")]
    [InlineData("content_json='PRIVATE-BAD-JSON'")]
    [InlineData("content_schema_version=99")]
    public async Task Cash_history_production_corruption_is_unavailable_without_disclosure_or_repair(string change)
    {
        await using var fixture = await Fixture.CreateAsync(); await CapturedItemHistory(fixture);
        var snapshot = (await fixture.Scalar("SELECT snapshot_id FROM production_order_snapshots LIMIT 1;"))!;
        await fixture.Execute("UPDATE production_order_snapshots SET " + change + ";");
        var before = await ProductionFingerprint(fixture);
        foreach (var endpoint in new[] { ProductionList, ProductionDetail })
        {
            var response = await fixture.Call(endpoint, routeValue: "CASH-ITEMS", snapshotId: snapshot, principal: LifecyclePrincipal());
            Assert.Equal(503, response.Status); Assert.Equal("no-store", response.CacheControl);
            Assert.True(response.Body.ValueKind == JsonValueKind.Undefined || !response.Body.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
        }
        Assert.Equal(before, await ProductionFingerprint(fixture));
    }
}
