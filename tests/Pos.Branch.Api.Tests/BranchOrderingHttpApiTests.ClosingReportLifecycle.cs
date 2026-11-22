using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private sealed record FrozenContextCycle(StoreLifecycleEndpoints.Request First, StoreLifecycleEndpoints.Request Middle,
        StoreLifecycleEndpoints.Request Last, JsonElement FirstLifecycle, string FirstPayload);

    private static async Task<FrozenContextCycle> CreateFrozenContextCycle(Fixture fixture)
    {
        var storeId = Guid.NewGuid().ToString();
        var firstShift = Guid.NewGuid().ToString();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), storeId, firstShift, "OpenStore", 500, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: LifecyclePrincipal())).Status);
        var first = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseShift", CashAmount = null };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", first, principal: LifecyclePrincipal())).Status);
        var firstDetail = await fixture.Call("GetClosingReportDetail", routeValue: first.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, firstDetail.Status);
        var original = firstDetail.Body.GetProperty("lifecycle").Clone();
        var payload = (await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE branch_id='BRANCH-01' AND request_id='{first.RequestId}';"))!;
        var secondOpen = open with { RequestId = Guid.NewGuid().ToString(), ShiftId = Guid.NewGuid().ToString(), Operation = "OpenShift", CashAmount = 200 };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", secondOpen, principal: LifecyclePrincipal())).Status);
        var middle = secondOpen with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseShift", CashAmount = 0 };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", middle, principal: LifecyclePrincipal())).Status);
        var thirdOpen = secondOpen with { RequestId = Guid.NewGuid().ToString(), ShiftId = Guid.NewGuid().ToString(), CashAmount = null };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", thirdOpen, principal: LifecyclePrincipal())).Status);
        var last = thirdOpen with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 500 };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", last, principal: LifecyclePrincipal())).Status);
        return new(first, middle, last, original, payload);
    }

    [Fact]
    public async Task Frozen_lifecycle_api_captures_all_shifts_and_distinguishes_uncounted_zero_and_informational_float()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cycle = await CreateFrozenContextCycle(fixture);
        var response = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        var lifecycle = response.Body.GetProperty("lifecycle");
        Assert.Equal(1, lifecycle.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("BRANCH-01", lifecycle.GetProperty("branchId").GetString());
        Assert.Equal(cycle.Last.RequestId, lifecycle.GetProperty("requestId").GetString());
        Assert.Equal(cycle.Last.StoreSessionId, lifecycle.GetProperty("storeSessionId").GetString());
        Assert.Equal(cycle.Last.ShiftId, lifecycle.GetProperty("closingShiftId").GetString());
        Assert.Equal(500, lifecycle.GetProperty("openingCashAmount").GetDecimal());
        Assert.True(lifecycle.GetProperty("closesStore").GetBoolean());
        Assert.Equal("EMP-2", lifecycle.GetProperty("opened").GetProperty("employeeId").GetString());
        Assert.Equal("DEV-1", lifecycle.GetProperty("opened").GetProperty("deviceId").GetString());
        var shifts = lifecycle.GetProperty("shifts").EnumerateArray().ToArray();
        Assert.Equal(new[] { cycle.First.ShiftId, cycle.Middle.ShiftId, cycle.Last.ShiftId }, shifts.Select(shift => shift.GetProperty("shiftId").GetString()));
        Assert.Equal(500, shifts[0].GetProperty("openingCashAmount").GetDecimal());
        Assert.Equal(200, shifts[1].GetProperty("openingCashAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, shifts[2].GetProperty("openingCashAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, shifts[0].GetProperty("closing").GetProperty("actualCashAmount").ValueKind);
        Assert.Equal("NotReconciled", shifts[0].GetProperty("closing").GetProperty("reconciliationStatus").GetString());
        Assert.Equal(0, shifts[1].GetProperty("closing").GetProperty("actualCashAmount").GetDecimal());
        Assert.Equal(-500, shifts[1].GetProperty("closing").GetProperty("cashDifference").GetDecimal());
        Assert.Equal("Reconciled", shifts[1].GetProperty("closing").GetProperty("reconciliationStatus").GetString());
        foreach (var shift in shifts) { Assert.Equal(500, shift.GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal()); }
        Assert.Equal(response.Body.GetProperty("snapshot").GetProperty("closing").GetRawText(), shifts[2].GetProperty("closing").GetRawText());
        Assert.Single(shifts, shift => shift.GetProperty("closing").GetProperty("actualCashAmount").ValueKind == JsonValueKind.Null);
        var early = await fixture.Call("GetClosingReportDetail", routeValue: cycle.First.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(cycle.FirstLifecycle.GetRawText(), early.Body.GetProperty("lifecycle").GetRawText());
        Assert.Single(early.Body.GetProperty("lifecycle").GetProperty("shifts").EnumerateArray());
        Assert.Equal(cycle.FirstPayload, await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE request_id='{cycle.First.RequestId}';"));
    }

    [Fact]
    public async Task Frozen_lifecycle_api_remains_original_after_new_store_master_changes_and_close_replay()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cycle = await CreateFrozenContextCycle(fixture);
        var before = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal());
        var originalLifecycle = before.Body.GetProperty("lifecycle").GetRawText();
        var originalPayload = await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE request_id='{cycle.Last.RequestId}';");
        var next = cycle.Last with { RequestId = Guid.NewGuid().ToString(), StoreSessionId = Guid.NewGuid().ToString(), ShiftId = Guid.NewGuid().ToString(), Operation = "OpenStore", CashAmount = 900 };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", next, principal: LifecyclePrincipal())).Status);
        await fixture.Execute("UPDATE master_data_packages SET lifecycle_status='Previous' WHERE master_data_version='OLD'; UPDATE master_data_packages SET lifecycle_status='Active' WHERE master_data_version='NEW';");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", cycle.Last, principal: LifecyclePrincipal())).Status);
        var restored = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(originalLifecycle, restored.Body.GetProperty("lifecycle").GetRawText());
        Assert.Equal(originalPayload, await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE request_id='{cycle.Last.RequestId}';"));
        Assert.Equal("3", await fixture.Scalar("SELECT COUNT(*) FROM closing_report_lifecycle_snapshots;"));
        Assert.Equal(next.StoreSessionId, (await fixture.Call("GetCurrentStoreLifecycle", principal: LifecyclePrincipal())).Body.GetProperty("storeSessionId").GetString());
    }

    [Fact]
    public async Task Frozen_lifecycle_api_is_scoped_even_when_another_branch_reuses_the_request_and_store_ids()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cycle = await CreateFrozenContextCycle(fixture);
        var now = DateTimeOffset.UtcNow;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var otherOpen = new StoreLifecycleCommand(Guid.NewGuid().ToString(), cycle.First.StoreSessionId, cycle.First.ShiftId,
            StoreLifecycleAction.OpenStore, 900, new("OTHER-EMPLOYEE", "OTHER-DEVICE", now, day));
        await store.ExecuteStoreLifecycleAsync("OTHER", otherOpen);
        await store.ExecuteStoreLifecycleAsync("OTHER", otherOpen with { RequestId = cycle.First.RequestId, Action = StoreLifecycleAction.CloseStore });
        var current = await fixture.Call("GetClosingReportDetail", routeValue: cycle.First.RequestId, principal: LifecyclePrincipal());
        var other = await fixture.Call("GetClosingReportDetail", routeValue: cycle.First.RequestId, principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(500, current.Body.GetProperty("lifecycle").GetProperty("openingCashAmount").GetDecimal());
        Assert.Equal(900, other.Body.GetProperty("lifecycle").GetProperty("openingCashAmount").GetDecimal());
        Assert.Equal("OTHER", other.Body.GetProperty("lifecycle").GetProperty("branchId").GetString());
        var missing = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(404, missing.Status);
        Assert.Equal("ClosingReportNotFound", missing.Body.GetProperty("error").GetString());
        foreach (var role in new[] { "Mobile", "Kitchen" })
        {
            Assert.Equal(403, (await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal(role))).Status);
        }
    }

    [Fact]
    public async Task Frozen_lifecycle_api_legacy_absence_remains_null_without_live_history_reconstruction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cycle = await CreateFrozenContextCycle(fixture);
        var before = await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE request_id='{cycle.Last.RequestId}';");
        await fixture.Execute($"DELETE FROM closing_report_context_outbox WHERE request_id='{cycle.Last.RequestId}';");
        await fixture.Execute($"DELETE FROM closing_report_lifecycle_snapshots WHERE request_id='{cycle.Last.RequestId}';");
        var result = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, result.Status);
        Assert.Equal(JsonValueKind.Null, result.Body.GetProperty("lifecycle").ValueKind);
        Assert.Equal(500, result.Body.GetProperty("snapshot").GetProperty("closing").GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(before, await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE request_id='{cycle.Last.RequestId}';"));
    }

    [Theory]
    [InlineData("BranchId", "\"OTHER\"")]
    [InlineData("RequestId", "\"PRIVATE-CORRUPT-REQUEST\"")]
    [InlineData("StoreSessionId", "\"PRIVATE-CORRUPT-STORE\"")]
    [InlineData("ClosingShiftId", "\"PRIVATE-CORRUPT-SHIFT\"")]
    [InlineData("Shifts", "[]")]
    public async Task Frozen_lifecycle_api_corrupt_companion_is_unavailable_without_private_error_text(string property, string replacement)
    {
        await using var fixture = await Fixture.CreateAsync();
        var cycle = await CreateFrozenContextCycle(fixture);
        await using var connection = new SqliteConnection($"Data Source={fixture.Path};Pooling=False");
        await connection.OpenAsync();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT context_json FROM closing_report_lifecycle_snapshots WHERE request_id=$request;";
        read.Parameters.AddWithValue("$request", cycle.Last.RequestId);
        var saved = JsonNode.Parse((string)(await read.ExecuteScalarAsync())!)!.AsObject();
        saved[property] = JsonNode.Parse(replacement);
        using var write = connection.CreateCommand();
        write.CommandText = "UPDATE closing_report_lifecycle_snapshots SET context_json=$json WHERE request_id=$request;";
        write.Parameters.AddWithValue("$json", saved.ToJsonString());
        write.Parameters.AddWithValue("$request", cycle.Last.RequestId);
        await write.ExecuteNonQueryAsync();
        var result = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(503, result.Status);
        Assert.Equal("no-store", result.CacheControl);
        Assert.DoesNotContain("PRIVATE", result.Body.ValueKind == JsonValueKind.Undefined ? string.Empty : result.Body.GetRawText());
    }

    [Fact]
    public async Task Frozen_lifecycle_api_context_insert_failure_rolls_back_close_and_retry_replays_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "OpenStore", 500, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: LifecyclePrincipal())).Status);
        await fixture.Execute("CREATE TRIGGER fail_frozen_context BEFORE INSERT ON closing_report_lifecycle_snapshots BEGIN SELECT RAISE(ABORT,'PRIVATE-CONTEXT-FAILURE'); END;");
        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore" };
        var failed = await fixture.Call("ExecuteStoreLifecycle", close, principal: LifecyclePrincipal());
        Assert.Equal(409, failed.Status);
        Assert.DoesNotContain("PRIVATE", failed.Body.GetRawText());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM store_lifecycle_commands;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM store_lifecycle_cash_snapshots;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM closing_report_outbox;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM closing_report_lifecycle_snapshots;"));
        var current = await fixture.Call("GetCurrentStoreLifecycle", principal: LifecyclePrincipal());
        Assert.False(current.Body.GetProperty("isClosed").GetBoolean());
        Assert.Equal(open.ShiftId, current.Body.GetProperty("activeShiftId").GetString());
        await fixture.Execute("DROP TRIGGER fail_frozen_context;");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: LifecyclePrincipal())).Status);
        var once = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: LifecyclePrincipal())).Status);
        var twice = await fixture.Call("GetClosingReportDetail", routeValue: close.RequestId, principal: LifecyclePrincipal());
        Assert.Equal(once.Body.GetProperty("lifecycle").GetRawText(), twice.Body.GetProperty("lifecycle").GetRawText());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM closing_report_lifecycle_snapshots;"));
    }

    [Fact]
    public async Task Frozen_lifecycle_api_reads_and_confirmed_delivery_preserve_context_and_v1_payload_bytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cycle = await CreateFrozenContextCycle(fixture);
        var detail = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal());
        var lifecycle = detail.Body.GetProperty("lifecycle").GetRawText();
        var payload = (await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE request_id='{cycle.Last.RequestId}';"))!;
        using var original = JsonDocument.Parse(payload);
        Assert.False(original.RootElement.TryGetProperty("lifecycle", out _));
        Assert.False(original.RootElement.GetProperty("cashSummary").TryGetProperty("Lifecycle", out _));
        var beforeReads = await fixture.Scalar("SELECT group_concat(request_id || ':' || attempt_count || ':' || coalesce(delivered_at,'')) FROM closing_report_outbox;");
        await fixture.Call("GetClosingReports", principal: LifecyclePrincipal());
        await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        Assert.Equal(beforeReads, await fixture.Scalar("SELECT group_concat(request_id || ':' || attempt_count || ':' || coalesce(delivered_at,'')) FROM closing_report_outbox;"));
        var transport = new FrozenContextAcceptedTransport();
        var worker = new ClosingReportDeliveryCycle(new SqliteTableSessionCommandStore(fixture.Path), transport, TimeProvider.System,
            Options.Create(new ClosingReportDeliveryOptions { Enabled = true, BranchId = "BRANCH-01" }));
        Assert.True(await worker.RunOnceAsync());
        Assert.True(await worker.RunOnceAsync());
        Assert.True(await worker.RunOnceAsync());
        Assert.False(await worker.RunOnceAsync());
        Assert.Contains(payload, transport.Payloads);
        var confirmed = await fixture.Call("GetClosingReportDetail", routeValue: cycle.Last.RequestId, principal: LifecyclePrincipal());
        Assert.Equal("Delivered", confirmed.Body.GetProperty("report").GetProperty("deliveryState").GetString());
        Assert.Equal(lifecycle, confirmed.Body.GetProperty("lifecycle").GetRawText());
        Assert.Equal(payload, await fixture.Scalar($"SELECT payload_json FROM closing_report_outbox WHERE request_id='{cycle.Last.RequestId}';"));
    }

    private sealed class FrozenContextAcceptedTransport : IClosingReportTransport
    {
        internal List<string> Payloads { get; } = [];
        public Task<ClosingReportDeliveryFailure?> SendAsync(ClosingReportLease report, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Payloads.Add(report.PayloadJson);
            return Task.FromResult<ClosingReportDeliveryFailure?>(null);
        }
    }
}
