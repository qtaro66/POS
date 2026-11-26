using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private const string HistoryShift = "GetCashHistoryShiftContext";
    private const string HistoryReceipt = "GetCashHistoryReceiptEvidence";

    private static async Task<string?> ContextFingerprint(Fixture fixture) => await CashHistoryFingerprint(fixture) + ":" +
        await fixture.Scalar("""
            SELECT coalesce((SELECT group_concat(command_json || result_json) FROM store_lifecycle_commands),'') || ':' ||
                coalesce((SELECT group_concat(summary_json) FROM store_lifecycle_cash_snapshots),'') || ':' ||
                (SELECT count(*) FROM closing_report_outbox) || ':' || (SELECT count(*) FROM print_jobs) || ':' ||
                coalesce((SELECT group_concat(table_session_id || status || version) FROM table_sessions),'');
            """);

    [Theory]
    [InlineData(HistoryShift)]
    [InlineData(HistoryReceipt)]
    public async Task Cash_history_context_routes_have_no_body_and_do_not_depend_on_cash_command_flag(string name)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new BranchOrderingRuntime(fixture.Path, TimeProvider.System));
        await using var app = builder.Build();
        app.MapBranchOrderingIfEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BranchOrdering:Enabled"] = "true", ["BranchOrdering:EnableCashSaleTest"] = "false" }).Build());
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
        var endpoint = endpoints
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
        Assert.Equal(0, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.Equal("GET", Assert.Single(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods));
        Assert.DoesNotContain(endpoints, value => value.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "CompleteBranchCashSale");
    }

    [Theory]
    [InlineData(HistoryShift)]
    [InlineData(HistoryReceipt)]
    public async Task Cash_history_context_requires_complete_pc_identity_and_captured_parent_scope(string name)
    {
        await using var fixture = await Fixture.CreateAsync(); var saved = await SavedCashHistory(fixture);
        var incomplete = LifecyclePrincipal(); var identity = (ClaimsIdentity)incomplete.Identity!;
        identity.RemoveClaim(identity.Claims.Single(claim => claim.Value == "EMP-1"));
        var before = await ContextFingerprint(fixture);
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), incomplete, new ClaimsPrincipal() })
        {
            var denied = await fixture.Call(name, routeValue: saved.Request, principal: principal);
            Assert.Equal(403, denied.Status); Assert.Equal("no-store", denied.CacheControl);
        }
        var foreign = await fixture.Call(name, routeValue: saved.Request, principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(404, foreign.Status); Assert.Equal("CashHistoryNotFound", foreign.Body.GetProperty("error").GetString());
        Assert.Equal("no-store", foreign.CacheControl);
        var missing = await fixture.Call(name, routeValue: "MISSING", principal: LifecyclePrincipal());
        Assert.Equal(404, missing.Status);
        Assert.Equal(before, await ContextFingerprint(fixture));
    }

    [Theory]
    [InlineData(HistoryShift, "?branchId=OTHER")]
    [InlineData(HistoryShift, "?shiftId=OTHER")]
    [InlineData(HistoryShift, "?pin=111111")]
    [InlineData(HistoryReceipt, "?receiptNumber=OTHER")]
    [InlineData(HistoryReceipt, "?fail=true")]
    [InlineData(HistoryReceipt, "?limit=1&limit=2")]
    public async Task Cash_history_context_rejects_all_query_overrides(string name, string query)
    {
        await using var fixture = await Fixture.CreateAsync(); var saved = await SavedCashHistory(fixture);
        var before = await ContextFingerprint(fixture);
        var response = await fixture.Call(name, routeValue: saved.Request, principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal("InvalidCashHistoryContextQuery", response.Body.GetProperty("error").GetString());
        Assert.Equal(before, await ContextFingerprint(fixture));
    }

    [Theory]
    [InlineData(HistoryShift)]
    [InlineData(HistoryReceipt)]
    public async Task Cash_history_context_rejects_noncanonical_parent_identifiers(string name)
    {
        await using var fixture = await Fixture.CreateAsync();
        foreach (var id in new[] { " bad", new string('x', 257), "bad\u0085id" })
        {
            var response = await fixture.Call(name, routeValue: id, principal: LifecyclePrincipal());
            Assert.Equal(400, response.Status); Assert.Equal("no-store", response.CacheControl);
            Assert.Equal("InvalidCashHistoryIdentity", response.Body.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task Cash_history_shift_context_keeps_recorded_shift_after_handover_and_new_store_without_writes()
    {
        await using var fixture = await Fixture.CreateAsync(); var principal = LifecyclePrincipal();
        var open = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 500, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", open, principal: principal)).Status);
        var cash = await fixture.Call("CompleteBranchCashSale", await PrepareLifecycleCash(fixture, "CONTEXT", "A01"), principal: principal);
        Assert.Equal(200, cash.Status);
        var close = open with { RequestId = Guid.NewGuid().ToString(), Operation = "CloseShift", CashAmount = null };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", close, principal: principal)).Status);
        var second = open with { RequestId = Guid.NewGuid().ToString(), ShiftId = Guid.NewGuid().ToString(), Operation = "OpenShift", CashAmount = 20 };
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", second, principal: principal)).Status);
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", second with
        { RequestId = Guid.NewGuid().ToString(), Operation = "CloseStore", CashAmount = 800 }, principal: principal)).Status);
        var nextStore = new StoreLifecycleEndpoints.Request(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), "OpenStore", 50, "111111");
        Assert.Equal(200, (await fixture.Call("ExecuteStoreLifecycle", nextStore, principal: principal)).Status);
        var before = await ContextFingerprint(fixture);
        var response = await fixture.Call(HistoryShift, routeValue: "CASH-CONTEXT", principal: principal);
        Assert.Equal(200, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(open.StoreSessionId, response.Body.GetProperty("storeSessionId").GetString());
        Assert.Equal(open.ShiftId, response.Body.GetProperty("shiftId").GetString());
        var entries = response.Body.GetProperty("entries").EnumerateArray().ToArray(); Assert.Equal(2, entries.Length);
        Assert.Equal("OpenStore", entries[0].GetProperty("action").GetString());
        Assert.Equal(500, entries[0].GetProperty("cashAmount").GetDecimal());
        Assert.Equal("CloseShift", entries[1].GetProperty("action").GetString());
        var closing = entries[1].GetProperty("closing");
        Assert.False(closing.GetProperty("closesStore").GetBoolean());
        Assert.Equal(720, closing.GetProperty("expectedCashAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, closing.GetProperty("actualCashAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, closing.GetProperty("cashDifference").ValueKind);
        Assert.DoesNotContain(nextStore.StoreSessionId, response.Body.GetRawText());
        Assert.DoesNotContain(second.ShiftId, response.Body.GetRawText());
        Assert.Equal(before, await ContextFingerprint(fixture));
        await fixture.Execute($"UPDATE store_lifecycle_commands SET command_json=json_set(command_json,'$.CashAmount',-1) WHERE request_id='{open.RequestId}';");
        var damaged = await ContextFingerprint(fixture);
        var unavailable = await fixture.Call(HistoryShift, routeValue: "CASH-CONTEXT", principal: principal);
        Assert.Equal(503, unavailable.Status); Assert.Equal("no-store", unavailable.CacheControl);
        Assert.Equal(JsonValueKind.Undefined, unavailable.Body.ValueKind);
        Assert.Equal(damaged, await ContextFingerprint(fixture));
        Assert.Equal(200, (await fixture.Call("GetCashHistoryDetail", routeValue: "CASH-CONTEXT", principal: principal)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cash_history_receipt_evidence_reads_recorded_simulation_without_rerendering_or_mutation(bool fail)
    {
        await using var fixture = await Fixture.CreateAsync(); await CapturedItemHistory(fixture);
        var beforeMissing = await ContextFingerprint(fixture);
        var initial = await fixture.Call(HistoryReceipt, routeValue: "CASH-ITEMS", principal: LifecyclePrincipal());
        Assert.Equal(200, initial.Status); Assert.Equal("no-store", initial.CacheControl);
        Assert.Equal(JsonValueKind.Null, initial.Body.GetProperty("simulation").ValueKind);
        Assert.Equal(beforeMissing, await ContextFingerprint(fixture));
        var receipt = initial.Body.GetProperty("receiptNumber").GetString()!;
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var original = (await store.SimulateReceiptAsync("BRANCH-01", receipt, fail, default))!;
        await fixture.Execute("UPDATE master_data_sellable_items SET display_name='PRIVATE-CURRENT'; UPDATE order_lines SET display_name='PRIVATE-CURRENT';");
        var before = await ContextFingerprint(fixture);
        var response = await fixture.Call(HistoryReceipt, routeValue: "CASH-ITEMS", principal: LifecyclePrincipal());
        Assert.Equal(200, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal("QueuedForSimulation", response.Body.GetProperty("capturedPrintStatus").GetString());
        var simulation = response.Body.GetProperty("simulation");
        Assert.Equal(original.Status, simulation.GetProperty("status").GetString());
        Assert.Equal(original.Text, simulation.GetProperty("text").GetString());
        Assert.DoesNotContain("PRIVATE", response.Body.GetRawText());
        Assert.Equal(before, await ContextFingerprint(fixture));
        var other = await SavedCashHistory(fixture);
        var otherResponse = await fixture.Call(HistoryReceipt, routeValue: other.Request, principal: LifecyclePrincipal());
        Assert.Equal(JsonValueKind.Null, otherResponse.Body.GetProperty("simulation").ValueKind);
        var legacy = await fixture.Call(HistoryShift, routeValue: "CASH-ITEMS", principal: LifecyclePrincipal());
        Assert.Equal(200, legacy.Status); Assert.Empty(legacy.Body.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Cash_history_context_corruption_is_generic_unavailable_and_does_not_repair_source()
    {
        await using var fixture = await Fixture.CreateAsync(); await CapturedItemHistory(fixture);
        var receipt = (await fixture.Scalar("SELECT receipt_number FROM simulated_cash_receipt_jobs LIMIT 1;"))!;
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        await store.SimulateReceiptAsync("BRANCH-01", receipt, true, default);
        await fixture.Execute("UPDATE test_receipt_simulation_results SET content='PRIVATE-INCONSISTENT';");
        var before = await ContextFingerprint(fixture);
        var response = await fixture.Call(HistoryReceipt, routeValue: "CASH-ITEMS", principal: LifecyclePrincipal());
        Assert.Equal(503, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(JsonValueKind.Undefined, response.Body.ValueKind);
        Assert.Equal("PRIVATE-INCONSISTENT", await fixture.Scalar("SELECT content FROM test_receipt_simulation_results LIMIT 1;"));
        Assert.Equal(before, await ContextFingerprint(fixture));
    }
}
