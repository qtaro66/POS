using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Fact]
    public async Task Session_detail_of_empty_open_table_has_no_bill_and_creates_no_preview_or_jobs()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        var before = await fixture.Scalar("SELECT COUNT(*) FROM command_receipts;");
        var detail = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1");
        Assert.Equal(200, detail.Status);
        Assert.Equal("no-store", detail.CacheControl);
        Assert.Equal("Ordering", detail.Body.GetProperty("session").GetProperty("status").GetString());
        Assert.Empty(detail.Body.GetProperty("batches").EnumerateArray());
        Assert.False(detail.Body.GetProperty("hasOpenCancellationRequest").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, detail.Body.GetProperty("billPreviewTotals").ValueKind);
        Assert.Equal(before, await fixture.Scalar("SELECT COUNT(*) FROM command_receipts;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM bill_preview_snapshots;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM print_jobs;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cloud_sync_outbox;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM table_session_audit;"));
    }

    [Fact]
    public async Task Session_detail_returns_accepted_batches_in_sequence_with_server_amounts_and_frozen_prices()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        await fixture.Call("SubmitBranchOrder", Submit("ORDER-Z"));
        await fixture.Call("SubmitBranchOrder", Submit("ORDER-A"));
        await fixture.Execute("UPDATE master_data_packages SET lifecycle_status='Previous' WHERE master_data_version='OLD'; UPDATE master_data_packages SET lifecycle_status='Active' WHERE master_data_version='NEW';");
        var detail = (await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1")).Body;
        var batches = detail.GetProperty("batches");
        Assert.Equal(2, batches.GetArrayLength());
        Assert.Equal("ORDER-Z", batches[0].GetProperty("requestId").GetString());
        Assert.Equal("ORDER-A", batches[1].GetProperty("requestId").GetString());
        Assert.True(batches[0].GetProperty("branchSequence").GetInt64() < batches[1].GetProperty("branchSequence").GetInt64());
        Assert.Equal("BRANCH-01:ORDER-Z:batch:1", batches[0].GetProperty("orderBatchId").GetString());
        Assert.Equal("BranchAccepted", batches[0].GetProperty("acceptanceStatus").GetString());
        Assert.Equal(220m, batches[0].GetProperty("activeItemNetAmount").GetDecimal());
        var lines = batches[0].GetProperty("lines");
        Assert.Equal("DineIn", lines[0].GetProperty("fulfillmentType").GetString());
        Assert.Equal("Takeaway", lines[1].GetProperty("fulfillmentType").GetString());
        Assert.Equal(100m, lines[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal("test", lines[0].GetProperty("comment").GetString());
        Assert.Equal("GROUP-1", lines[0].GetProperty("options")[0].GetProperty("optionGroupId").GetString());
        Assert.Equal(10m, lines[0].GetProperty("options")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(110m, lines[0].GetProperty("activeGrossAmount").GetDecimal());
        var totals = detail.GetProperty("billPreviewTotals");
        Assert.Equal("OLD", totals.GetProperty("masterDataVersion").GetString());
        Assert.Equal(440m, totals.GetProperty("netTotal").GetDecimal());
        Assert.Equal(28.79m, totals.GetProperty("vatInclusiveAmount").GetDecimal());
        Assert.Equal("2", await fixture.Scalar("SELECT COUNT(*) FROM print_jobs;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM bill_preview_snapshots;"));
        Assert.Equal("3", await fixture.Scalar("SELECT COUNT(*) FROM command_receipts;"));
    }

    [Fact]
    public async Task Session_detail_preserves_cancelled_lines_and_uses_active_quantities_discounts_and_rounding()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"));
        // Seed persisted financial/cancellation state, not a test of cancellation/discount command workflows.
        await fixture.Execute("""
            UPDATE order_lines SET original_quantity=3, active_quantity=2,
                discount_kind='Percentage', discount_requested_value='10' WHERE line_sequence=1;
            UPDATE order_lines SET active_quantity=0, status='Cancelled' WHERE line_sequence=2;
            UPDATE table_session_billing_policy SET bill_discount_kind='FixedAmount', bill_discount_requested_value='8.9';
            """);
        var detail = (await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1")).Body;
        var batch = detail.GetProperty("batches")[0];
        var lines = batch.GetProperty("lines");
        Assert.Equal(3, lines[0].GetProperty("originalQuantity").GetInt32());
        Assert.Equal(2, lines[0].GetProperty("activeQuantity").GetInt32());
        Assert.Equal(220m, lines[0].GetProperty("activeGrossAmount").GetDecimal());
        Assert.Equal(22m, lines[0].GetProperty("activeDiscountAmount").GetDecimal());
        Assert.Equal("Cancelled", lines[1].GetProperty("status").GetString());
        Assert.Equal(0m, lines[1].GetProperty("activeNetAmount").GetDecimal());
        Assert.Equal(198m, batch.GetProperty("activeItemNetAmount").GetDecimal());
        var totals = detail.GetProperty("billPreviewTotals");
        Assert.Equal(8.9m, totals.GetProperty("billDiscountAmount").GetDecimal());
        Assert.Equal(189.1m, totals.GetProperty("totalBeforeRounding").GetDecimal());
        Assert.Equal(-0.1m, totals.GetProperty("roundingAdjustment").GetDecimal());
        Assert.Equal(189m, totals.GetProperty("netTotal").GetDecimal());
        await fixture.Execute("UPDATE order_lines SET active_quantity=0, status='Cancelled';");
        var cancelled = (await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1")).Body;
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cancelled.GetProperty("billPreviewTotals").ValueKind);
        Assert.Equal(0m, cancelled.GetProperty("batches")[0].GetProperty("activeItemNetAmount").GetDecimal());
        Assert.Equal(2, cancelled.GetProperty("batches")[0].GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task Session_detail_is_branch_scoped_requires_identity_and_excludes_closed_sessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        Assert.Equal(401, (await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1", principal: new ClaimsPrincipal())).Status);
        Assert.Equal(404, (await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1", principal: Principal("OTHER-BRANCH"))).Status);
        Assert.Equal(404, (await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "MISSING")).Status);
        await fixture.Execute("UPDATE table_sessions SET status='PaymentPending'; INSERT INTO cancellation_requests VALUES ('BRANCH-01','CANCEL-1','SESSION-1','ProductionStatusChecking');");
        var pending = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1");
        Assert.Equal(200, pending.Status);
        Assert.True(pending.Body.GetProperty("hasOpenCancellationRequest").GetBoolean());
        Assert.Equal("PaymentPending", await fixture.Scalar("SELECT status FROM table_sessions;"));
        await fixture.Execute("UPDATE table_sessions SET status='Closed';");
        Assert.Equal(404, (await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1")).Status);
    }

    [Fact]
    public async Task Session_detail_fails_closed_when_stored_batch_has_no_accepted_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"));
        await fixture.Execute("DELETE FROM command_receipts WHERE request_id='ORDER-1';");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM order_batches;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM bill_preview_snapshots;"));
    }

    [Fact]
    public async Task Session_detail_query_rejects_non_utc_time_and_never_creates_missing_database()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = new SqliteBranchOrderingQuery(fixture.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => query.DetailAsync("BRANCH-01", "SESSION-1", DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(7))));
        var missing = fixture.Path + ".missing";
        await Assert.ThrowsAsync<SqliteException>(() => new SqliteBranchOrderingQuery(missing).DetailAsync("BRANCH-01", "SESSION-1", DateTimeOffset.UtcNow));
        Assert.False(File.Exists(missing));
    }
}
