using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Pos.Application.Security;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private const string QueueReprint = "QueueKitchenReprint";
    private const string ReprintStatus = "GetKitchenReprintStatus";
    private sealed class ReprintPins : IBranchEmployeePinValidator
    {
        private int _calls;
        public int Calls => _calls;
        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(string branchId, ReadOnlyMemory<char> pin, CancellationToken token = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(pin.Span.SequenceEqual("111111") ? new VerifiedBranchEmployee(branchId, "EMP-2") :
                pin.Span.SequenceEqual("222222") ? new VerifiedBranchEmployee(branchId, "EMP-3") : null);
        }
    }
    private static async Task AllowReprint(Fixture fixture, bool allowed = true, int revision = 2)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($$"""
            {"schemaVersion":1,"branchId":"BRANCH-01","revision":{{revision}},"employees":[
              {"employeeId":"EMP-2","active":true,"setSoldOut":false,"openStore":true,"reprintKitchenOrder":{{allowed.ToString().ToLowerInvariant()}}}]}
            """));
        Assert.True(await fixture.Permissions.ApplyAsync(stream, default));
    }
    private static async Task<(string Cash, string Snapshot, string Line, string Job)> ReprintSource(Fixture fixture, bool cancelled = false)
    {
        if (cancelled) { await CaptureCashAfterCancellation(fixture, false); }
        else { await CapturedItemHistory(fixture); }
        return (cancelled ? "CASH-EVENTS" : "CASH-ITEMS",
            (await fixture.Scalar("SELECT snapshot_id FROM production_order_snapshots LIMIT 1;"))!,
            (await fixture.Scalar("SELECT order_line_id FROM order_lines ORDER BY line_sequence LIMIT 1;"))!,
            (await fixture.Scalar("SELECT print_job_id FROM print_jobs WHERE purpose='KitchenOrder' LIMIT 1;"))!);
    }
    private static KitchenReprintEndpoints.Request ReprintRequest(string job, string? request = null, string pin = "111111", string reason = "Customer requested a clearly marked copy") =>
        new(1, request ?? Guid.NewGuid().ToString("D"), job, reason, pin);
    private static Task<(int Status, JsonElement Body, string CacheControl)> Queue(Fixture fixture,
        (string Cash, string Snapshot, string Line, string Job) source, KitchenReprintEndpoints.Request request,
        ClaimsPrincipal? principal = null, string? query = null) => fixture.Call(QueueReprint, request,
            source.Cash, principal ?? LifecyclePrincipal(), query: query, snapshotId: source.Snapshot, orderLineId: source.Line);
    private static Task<string?> ReprintBusinessFingerprint(Fixture fixture) => fixture.Scalar("""
        SELECT coalesce((SELECT group_concat(result_json || bill_snapshot_json) FROM cash_sales),'') || ':' ||
            coalesce((SELECT group_concat(order_line_id || active_quantity || original_quantity || display_name || unit_price) FROM order_lines),'') || ':' ||
            coalesce((SELECT group_concat(content_json || content_sha256) FROM production_order_snapshots),'') || ':' ||
            coalesce((SELECT group_concat(command_json || result_json) FROM store_lifecycle_commands),'') || ':' ||
            (SELECT count(*) FROM cloud_sync_outbox) || ':' || (SELECT count(*) FROM table_session_audit) || ':' ||
            (SELECT count(*) FROM cancellation_confirmations);
        """);

    [Fact]
    public async Task Kitchen_reprint_queue_is_idempotent_pin_scoped_and_preserves_original_business_data()
    {
        var pins = new ReprintPins(); await using var fixture = await Fixture.CreateAsync(pinValidator: pins);
        var source = await ReprintSource(fixture); await AllowReprint(fixture);
        var before = await ReprintBusinessFingerprint(fixture); var request = ReprintRequest(source.Job);
        var first = await Queue(fixture, source, request);
        Assert.Equal(200, first.Status); Assert.Equal("no-store", first.CacheControl);
        Assert.Equal(source.Snapshot, first.Body.GetProperty("originalSnapshotId").GetString());
        Assert.Equal(source.Job, first.Body.GetProperty("originalPrintJobId").GetString());
        Assert.Equal(source.Line, first.Body.GetProperty("orderLineId").GetString());
        Assert.Equal("KITCHEN-1", first.Body.GetProperty("productionStationId").GetString());
        var replay = await Queue(fixture, source, request with { CommandRequestId = request.CommandRequestId.ToUpperInvariant() });
        Assert.Equal(200, replay.Status); Assert.Equal(first.Body.GetRawText(), replay.Body.GetRawText()); Assert.Equal(2, pins.Calls);
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_requests;"));
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_audit;"));
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM print_jobs WHERE is_reprint=1;"));
        Assert.Equal("EMP-1", await fixture.Scalar("SELECT requester_employee_id FROM kitchen_reprint_requests;"));
        Assert.Equal("EMP-2", await fixture.Scalar("SELECT approver_employee_id FROM kitchen_reprint_requests;"));
        Assert.Equal(before, await ReprintBusinessFingerprint(fixture));
        Assert.DoesNotContain("111111", first.Body.GetRawText()); Assert.DoesNotContain("111111", request.ToString());
        var status = await fixture.Call(ReprintStatus, routeValue: request.CommandRequestId.ToUpperInvariant(), principal: LifecyclePrincipal());
        Assert.Equal(200, status.Status); Assert.Equal("no-store", status.CacheControl);
        Assert.Equal("PrintQueued", status.Body.GetProperty("status").GetString()); Assert.Equal(2, pins.Calls);
        await fixture.Execute("UPDATE print_jobs SET status='Unknown' WHERE is_reprint=1;");
        var unknown = await fixture.Call(ReprintStatus, routeValue: request.CommandRequestId, principal: LifecyclePrincipal());
        Assert.Equal("Unknown", unknown.Body.GetProperty("status").GetString());
        Assert.Equal(200, (await Queue(fixture, source, request)).Status);
        Assert.Equal("Unknown", await fixture.Scalar("SELECT status FROM print_jobs WHERE is_reprint=1;"));
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_audit;"));
        await AllowReprint(fixture, false, 3);
        Assert.Equal(403, (await Queue(fixture, source, request)).Status);
        Assert.Equal(4, pins.Calls);
        Assert.Equal(200, (await fixture.Call(ReprintStatus, routeValue: request.CommandRequestId, principal: LifecyclePrincipal())).Status);
    }

    [Fact]
    public async Task Kitchen_reprint_corrupt_record_returns_unavailable_without_requeue_or_details()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await ReprintSource(fixture); await AllowReprint(fixture);
        var request = ReprintRequest(source.Job); Assert.Equal(200, (await Queue(fixture, source, request)).Status);
        await fixture.Execute("DROP TRIGGER kitchen_reprint_requests_no_update; UPDATE kitchen_reprint_requests SET receipt_json=json_set(receipt_json,'$.CashRequestId','corrupt-target');");
        var status = await fixture.Call(ReprintStatus, routeValue: request.CommandRequestId, principal: LifecyclePrincipal());
        Assert.Equal(503, status.Status); Assert.Equal("no-store", status.CacheControl);
        Assert.Equal(503, (await Queue(fixture, source, request)).Status);
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_audit;"));
    }

    [Fact]
    public async Task Kitchen_reprint_can_use_a_fully_cancelled_original_line_not_the_chargeable_bill()
    {
        await using var fixture = await Fixture.CreateAsync(cancellationEnabled: true);
        var source = await ReprintSource(fixture, true); await AllowReprint(fixture);
        var bill = await fixture.Call("GetCashHistoryDetail", routeValue: source.Cash, principal: LifecyclePrincipal());
        Assert.DoesNotContain(bill.Body.GetProperty("bill").GetProperty("items").EnumerateArray(),
            line => line.GetProperty("orderLineId").GetString() == source.Line);
        var before = await ReprintBusinessFingerprint(fixture);
        var queued = await Queue(fixture, source, ReprintRequest(source.Job));
        Assert.Equal(200, queued.Status);
        var payload = JsonDocument.Parse((await fixture.Scalar("SELECT content_json FROM kitchen_reprint_snapshots;"))!);
        using (payload)
        {
            var line = Assert.Single(payload.RootElement.GetProperty("lines").EnumerateArray());
            Assert.Equal(source.Line, line.GetProperty("orderLineId").GetString());
            Assert.Equal(1, line.GetProperty("quantity").GetInt32());
            Assert.Equal("Test item", line.GetProperty("displayName").GetString());
            Assert.Equal("Test option", Assert.Single(line.GetProperty("options").EnumerateArray()).GetProperty("displayName").GetString());
        }
        Assert.Equal(before, await ReprintBusinessFingerprint(fixture));
    }

    [Theory]
    [InlineData("000000", 401)]
    [InlineData("222222", 403)]
    [InlineData("", 400)]
    public async Task Kitchen_reprint_requires_a_fresh_active_authorized_pin(string pin, int expected)
    {
        await using var fixture = await Fixture.CreateAsync(); var source = await ReprintSource(fixture); await AllowReprint(fixture);
        var response = await Queue(fixture, source, ReprintRequest(source.Job, pin: pin));
        Assert.Equal(expected, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_requests;"));
    }

    [Theory]
    [InlineData("Mobile")]
    [InlineData("Kitchen")]
    [InlineData("Other")]
    public async Task Kitchen_reprint_queue_and_status_deny_non_pc_devices_before_pin(string role)
    {
        var pins = new ReprintPins(); await using var fixture = await Fixture.CreateAsync(pinValidator: pins);
        var source = await ReprintSource(fixture); await AllowReprint(fixture);
        Assert.Equal(403, (await Queue(fixture, source, ReprintRequest(source.Job), LifecyclePrincipal(role))).Status);
        Assert.Equal(403, (await fixture.Call(ReprintStatus, routeValue: Guid.NewGuid().ToString(), principal: LifecyclePrincipal(role))).Status);
        Assert.Equal(0, pins.Calls); Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_requests;"));
    }

    [Fact]
    public async Task Kitchen_reprint_status_is_requester_and_device_scoped_without_another_pin()
    {
        var pins = new ReprintPins(); await using var fixture = await Fixture.CreateAsync(pinValidator: pins);
        var source = await ReprintSource(fixture); await AllowReprint(fixture); var request = ReprintRequest(source.Job);
        Assert.Equal(200, (await Queue(fixture, source, request)).Status);
        foreach (var changed in new[] { BranchPosClaimTypes.EmployeeId, BranchPosClaimTypes.DeviceId, BranchPosClaimTypes.BranchId })
        {
            var principal = LifecyclePrincipal(); var identity = (ClaimsIdentity)principal.Identity!;
            identity.RemoveClaim(identity.Claims.Single(claim => claim.Type == changed)); identity.AddClaim(new(changed, "OTHER"));
            var missing = await fixture.Call(ReprintStatus, routeValue: request.CommandRequestId, principal: principal);
            Assert.Equal(404, missing.Status); Assert.Equal("no-store", missing.CacheControl);
        }
        Assert.Equal(1, pins.Calls);
        Assert.Equal(404, (await fixture.Call(ReprintStatus, routeValue: Guid.NewGuid().ToString(), principal: LifecyclePrincipal())).Status);
    }

    [Fact]
    public async Task Kitchen_reprint_replay_binds_reason_line_source_and_approver_without_extra_jobs()
    {
        await using var fixture = await Fixture.CreateAsync(); var source = await ReprintSource(fixture); await AllowReprint(fixture);
        var request = ReprintRequest(source.Job); Assert.Equal(200, (await Queue(fixture, source, request)).Status);
        Assert.Equal(409, (await Queue(fixture, source, request with { Reason = "Different reason" })).Status);
        Assert.Equal(409, (await Queue(fixture, source with { Line = "OTHER-LINE" }, request)).Status);
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM print_jobs WHERE is_reprint=1;"));
        Assert.Equal("1", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_audit;"));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("command")]
    [InlineData("reason-blank")]
    [InlineData("reason-control")]
    [InlineData("reason-long")]
    [InlineData("pin-long")]
    [InlineData("snapshot-long")]
    [InlineData("line-control")]
    public async Task Kitchen_reprint_rejects_invalid_command_inputs_before_any_write(string fault)
    {
        await using var fixture = await Fixture.CreateAsync(); var source = await ReprintSource(fixture); await AllowReprint(fixture);
        var request = ReprintRequest(source.Job);
        switch (fault)
        {
            case "schema": request = request with { SchemaVersion = 2 }; break;
            case "command": request = request with { CommandRequestId = "not-a-guid" }; break;
            case "reason-blank": request = request with { Reason = " \t" }; break;
            case "reason-control": request = request with { Reason = "bad\u0085reason" }; break;
            case "reason-long": request = request with { Reason = new string('x', 4097) }; break;
            case "pin-long": request = request with { Pin = new string('1', 129) }; break;
            case "snapshot-long": source.Snapshot = new string('x', 1025); break;
            case "line-control": source.Line = "bad\u0085line"; break;
        }
        Assert.Equal(400, (await Queue(fixture, source, request)).Status);
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_requests;"));
    }

    [Fact]
    public async Task Kitchen_reprint_bounded_reason_preserves_plaintext_and_query_overrides_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync(); var source = await ReprintSource(fixture); await AllowReprint(fixture);
        var request = ReprintRequest(source.Job, reason: new string('ก', 4093) + "\r\n\t");
        Assert.Equal(400, (await Queue(fixture, source, request, query: "?branchId=OTHER")).Status);
        Assert.Equal(200, (await Queue(fixture, source, request)).Status);
        Assert.Equal(request.Reason, await fixture.Scalar("SELECT reason FROM kitchen_reprint_requests;"));
        Assert.Equal(400, (await fixture.Call(ReprintStatus, routeValue: request.CommandRequestId, principal: LifecyclePrincipal(), query: "?pin=ignored")).Status);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("alias")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    public async Task Kitchen_reprint_request_local_json_contract_rejects_ambiguity_and_actor_overrides(string fault)
    {
        await using var fixture = await Fixture.CreateAsync(); var source = await ReprintSource(fixture); await AllowReprint(fixture);
        var request = ReprintRequest(source.Job); var json = JsonSerializer.Serialize(request);
        json = fault switch
        {
            "duplicate" => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal),
            "alias" => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"SchemaVersion\":1", StringComparison.Ordinal),
            "unknown" => json.Replace("{", "{\"approverEmployeeId\":\"OTHER\",", StringComparison.Ordinal),
            "missing" => json.Replace("\"schemaVersion\":1,", "", StringComparison.Ordinal),
            _ => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\"", StringComparison.Ordinal)
        };
        var response = await fixture.Call(QueueReprint, routeValue: source.Cash, snapshotId: source.Snapshot,
            orderLineId: source.Line, principal: LifecyclePrincipal(), rawJson: json);
        Assert.Equal(400, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal("0", await fixture.Scalar("SELECT count(*) FROM kitchen_reprint_requests;"));
    }
}
