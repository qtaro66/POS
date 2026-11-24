using System.Globalization;
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
    private static string CashHistoryDay(DateTimeOffset? instant = null) =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(instant ?? DateTimeOffset.UtcNow, "Asia/Bangkok").ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<(string Request, string Session, string Receipt)> SavedCashHistory(Fixture fixture,
        string branch = "BRANCH-01", string? day = null, string? receipt = null, bool financials = true, bool zero = false)
    {
        var request = Guid.NewGuid().ToString(); var session = Guid.NewGuid().ToString();
        var number = receipt ?? "TEST-" + Guid.NewGuid().ToString("N");
        var result = new CashSaleResult(session, "HISTORICAL-EMPLOYEE", "HISTORICAL-DEVICE", 3,
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), day ?? CashHistoryDay(), zero ? 0 : 150, zero ? 0 : 110,
            zero ? 0 : 40, number, "QueuedForSimulation");
        var bill = financials && !zero ? """
            {"itemGrossAmount":120,"lineDiscountAmount":10,"billDiscount":{"appliedAmount":5},
             "serviceChargeAmount":5,"totalBeforeRounding":110,"roundingAdjustment":0,"netTotal":110,
             "vatInclusiveAmount":7.2,"amountBeforeVat":102.8}
            """ : "{}";
        static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
        await fixture.Execute($"""
            INSERT INTO table_sessions VALUES ('{branch}','{session}','ORIGINAL-TABLE','Completed',5);
            INSERT INTO cash_sales VALUES ('{branch}','{request}','{session}','{Quote(JsonSerializer.Serialize(result))}','{bill}');
            INSERT INTO simulated_cash_receipt_jobs VALUES ('{branch}','{request}','{Quote(number)}','{DateTimeOffset.UtcNow.AddMinutes(-1):O}');
            """);
        return (request, session, number);
    }

    private static Task<string?> CashHistoryFingerprint(Fixture fixture) => fixture.Scalar("""
        SELECT coalesce((SELECT group_concat(branch_id || request_id || table_session_id || result_json || bill_snapshot_json) FROM cash_sales),'')
            || ':' || (SELECT count(*) FROM table_session_audit)
            || ':' || (SELECT count(*) FROM cloud_sync_outbox)
            || ':' || (SELECT count(*) FROM simulated_cash_receipt_jobs)
            || ':' || (SELECT count(*) FROM sqlite_master WHERE name='test_receipt_simulation_results');
        """);

    [Theory]
    [InlineData("GetCashHistory")]
    [InlineData("GetCashHistoryDetail")]
    public async Task Cash_history_requires_complete_pc_identity_without_pin_and_returns_no_store(string endpoint)
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = await SavedCashHistory(fixture);
        var incomplete = LifecyclePrincipal();
        var identity = (ClaimsIdentity)incomplete.Identity!;
        identity.RemoveClaim(identity.Claims.Single(claim => claim.Value == "EMP-1"));
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), incomplete, new ClaimsPrincipal() })
        {
            var response = await fixture.Call(endpoint, principal: principal, routeValue: saved.Request);
            Assert.Equal(403, response.Status); Assert.Equal("no-store", response.CacheControl);
        }
        var accepted = await fixture.Call(endpoint, principal: LifecyclePrincipal(), routeValue: saved.Request);
        Assert.Equal(200, accepted.Status); Assert.Equal("no-store", accepted.CacheControl);
    }

    [Fact]
    public async Task Cash_history_is_branch_scoped_and_projects_only_captured_amounts_without_mutations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = await SavedCashHistory(fixture); await SavedCashHistory(fixture, "OTHER");
        var before = await CashHistoryFingerprint(fixture);
        var list = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal());
        Assert.Equal(200, list.Status); Assert.Equal(1, list.Body.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("BRANCH-01", list.Body.GetProperty("branchId").GetString());
        Assert.Equal(CashHistoryDay(), list.Body.GetProperty("businessDate").GetString());
        var item = Assert.Single(list.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(saved.Request, item.GetProperty("requestId").GetString());
        Assert.Equal(saved.Receipt, item.GetProperty("receiptNumber").GetString());
        Assert.Equal("Cash", item.GetProperty("paymentMethod").GetString());
        Assert.Equal("Completed", item.GetProperty("status").GetString());
        Assert.Equal(150, item.GetProperty("tenderedAmount").GetDecimal());
        Assert.Equal(110, item.GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(40, item.GetProperty("changeAmount").GetDecimal());
        Assert.Equal("QueuedForSimulation", item.GetProperty("capturedPrintStatus").GetString());
        var first = await fixture.Call("GetCashHistoryDetail", routeValue: saved.Request, principal: LifecyclePrincipal());
        Assert.Equal(item.GetRawText(), first.Body.GetProperty("transaction").GetRawText());
        Assert.Equal(110, first.Body.GetProperty("financials").GetProperty("amountDue").GetDecimal());
        await fixture.Execute("UPDATE master_data_sellable_items SET display_name='CHANGED-MASTER',resolved_price='999'; UPDATE table_sessions SET table_id='MOVED-TABLE';");
        var second = await fixture.Call("GetCashHistoryDetail", routeValue: saved.Request, principal: LifecyclePrincipal());
        Assert.Equal(first.Body.GetProperty("transaction").GetRawText(), second.Body.GetProperty("transaction").GetRawText());
        Assert.Equal(first.Body.GetProperty("financials").GetRawText(), second.Body.GetProperty("financials").GetRawText());
        Assert.DoesNotContain("CHANGED-MASTER", second.Body.GetRawText());
        Assert.DoesNotContain("MOVED-TABLE", second.Body.GetRawText());
        Assert.DoesNotContain("resultJson", second.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var foreign = await fixture.Call("GetCashHistoryDetail", routeValue: saved.Request, principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(404, foreign.Status); Assert.Equal("CashHistoryNotFound", foreign.Body.GetProperty("error").GetString());
        Assert.Equal(before, await CashHistoryFingerprint(fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cash_history_preserves_missing_financials_and_zero_cash_without_reconstructing_them(bool zero)
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = await SavedCashHistory(fixture, financials: false, zero: zero);
        var detail = await fixture.Call("GetCashHistoryDetail", routeValue: saved.Request, principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status); Assert.Equal(JsonValueKind.Null, detail.Body.GetProperty("financials").ValueKind);
        Assert.Equal(zero ? 0 : 110, detail.Body.GetProperty("transaction").GetProperty("appliedAmount").GetDecimal());
    }

    [Theory]
    [InlineData("?businessDate=2026-02-30")]
    [InlineData("?businessDate=2026-1-1")]
    [InlineData("?businessDate=")]
    [InlineData("?receiptNumber=%20TEST")]
    [InlineData("?receiptNumber=TEST%20")]
    [InlineData("?receiptNumber=%0A")]
    [InlineData("?tableSessionId=%20SESSION-1")]
    [InlineData("?paymentMethod=cash")]
    [InlineData("?paymentMethod=Card")]
    [InlineData("?status=completed")]
    [InlineData("?status=Refunded")]
    [InlineData("?status=All&status=Completed")]
    [InlineData("?Status=All")]
    [InlineData("?branchId=OTHER")]
    [InlineData("?limit=0")]
    [InlineData("?limit=51")]
    [InlineData("?limit=1.5")]
    [InlineData("?cursor=bad-cursor")]
    public async Task Cash_history_rejects_noncanonical_ambiguous_or_cross_branch_queries(string query)
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, response.Status); Assert.Equal("InvalidCashHistoryQuery", response.Body.GetProperty("error").GetString());
        Assert.Equal("no-store", response.CacheControl);
    }

    [Fact]
    public async Task Cash_history_literal_receipt_filter_and_high_water_cursor_keep_the_original_scope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await SavedCashHistory(fixture, receipt: "TEST-%_ONE");
        var second = await SavedCashHistory(fixture, receipt: "TEST-%_TWO");
        await SavedCashHistory(fixture, receipt: "TEST-OTHER");
        var filter = "?receiptNumber=%25_&paymentMethod=Cash&status=Completed&limit=1";
        var page = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: filter);
        Assert.Equal(200, page.Status);
        Assert.Equal(second.Request, Assert.Single(page.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        var cursor = page.Body.GetProperty("nextCursor").GetString()!;
        await SavedCashHistory(fixture, receipt: "TEST-%_APPENDED");
        var next = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: filter + "&cursor=" + cursor);
        Assert.Equal(200, next.Status);
        Assert.Equal(first.Request, Assert.Single(next.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
        Assert.Equal(JsonValueKind.Null, next.Body.GetProperty("nextCursor").ValueKind);
        foreach (var changed in new[] { filter.Replace("%25_", "OTHER"), filter.Replace("limit=1", "limit=2"), filter.Replace("paymentMethod=Cash", "paymentMethod=All") })
        {
            Assert.Equal(400, (await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: changed + "&cursor=" + cursor)).Status);
        }
        Assert.Equal(400, (await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(branch: "OTHER"), query: filter + "&cursor=" + cursor)).Status);
        var bySession = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?tableSessionId=" + second.Session);
        Assert.Equal(second.Request, Assert.Single(bySession.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task Cash_history_defaults_to_branch_local_business_date_without_using_an_older_open_store()
    {
        await using var fixture = await Fixture.CreateAsync();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1); var day = CashHistoryDay(yesterday);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        await store.ExecuteStoreLifecycleAsync("BRANCH-01", new StoreLifecycleCommand(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(), StoreLifecycleAction.OpenStore, 500, new("EMP", "DEVICE", yesterday, DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture))));
        await SavedCashHistory(fixture, day: day); var saved = await SavedCashHistory(fixture);
        var page = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal());
        Assert.Equal(200, page.Status); Assert.Equal(CashHistoryDay(), page.Body.GetProperty("businessDate").GetString());
        Assert.Equal(saved.Request, Assert.Single(page.Body.GetProperty("items").EnumerateArray()).GetProperty("requestId").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cash_history_missing_capture_or_corrupt_settlement_is_unavailable_without_error_disclosure(bool missing)
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = await SavedCashHistory(fixture);
        await fixture.Execute(missing ? "DELETE FROM simulated_cash_receipt_jobs;" : "UPDATE cash_sales SET result_json=json_set(result_json,'$.ChangeAmount',-1);");
        var before = await CashHistoryFingerprint(fixture);
        foreach (var endpoint in new[] { "GetCashHistory", "GetCashHistoryDetail" })
        {
            var response = await fixture.Call(endpoint, routeValue: saved.Request, principal: LifecyclePrincipal());
            Assert.Equal(503, response.Status); Assert.Equal("no-store", response.CacheControl);
            Assert.Equal(JsonValueKind.Undefined, response.Body.ValueKind);
        }
        Assert.Equal(before, await CashHistoryFingerprint(fixture));
    }

    [Fact]
    public async Task Cash_history_reads_the_original_completed_cash_sale_with_legacy_non_guid_identifiers()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(200, (await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"))).Status);
        Assert.Equal(200, (await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"))).Status);
        var session = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1");
        var payment = await fixture.Call("RequestBranchTablePayment", new
        {
            schemaVersion = 1,
            context = Context("PAY-1", "SESSION-1"),
            expectedVersion = session.Body.GetProperty("session").GetProperty("tableVersion").GetInt64()
        });
        Assert.Equal(200, payment.Status);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var day = CashHistoryDay();
        Assert.True(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-1", "STORE-1",
            DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture), 500, default));
        var completed = await fixture.Call("CompleteBranchCashSale", new
        {
            schemaVersion = 1,
            context = Context("CASH-1", "SESSION-1"),
            expectedVersion = payment.Body.GetProperty("result").GetProperty("tableVersion").GetInt64(),
            tenderedAmount = 300m
        }, principal: LifecyclePrincipal());
        Assert.Equal(200, completed.Status);
        var before = await CashHistoryFingerprint(fixture);
        var list = await fixture.Call("GetCashHistory", principal: LifecyclePrincipal(), query: "?tableSessionId=SESSION-1");
        Assert.Equal(200, list.Status);
        var item = Assert.Single(list.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("CASH-1", item.GetProperty("requestId").GetString());
        Assert.Equal(completed.Body.GetProperty("receiptNumber").GetString(), item.GetProperty("receiptNumber").GetString());
        Assert.Equal(completed.Body.GetProperty("appliedAmount").GetDecimal(), item.GetProperty("appliedAmount").GetDecimal());
        var detail = await fixture.Call("GetCashHistoryDetail", routeValue: "CASH-1", principal: LifecyclePrincipal());
        Assert.Equal(200, detail.Status); Assert.Equal("SHIFT-1", detail.Body.GetProperty("transaction").GetProperty("shiftId").GetString());
        Assert.Equal(before, await CashHistoryFingerprint(fixture));
    }

    [Fact]
    public async Task Cash_history_routes_are_read_only_and_not_gated_by_cash_sale_test_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new BranchOrderingRuntime(fixture.Path, TimeProvider.System));
        await using var app = builder.Build();
        app.MapBranchOrderingIfEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BranchOrdering:Enabled"] = "true",
            ["BranchOrdering:EnableCashSaleTest"] = "false"
        }).Build());
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
        foreach (var name in new[] { "GetCashHistory", "GetCashHistoryDetail" })
        {
            var endpoint = Assert.Single(endpoints, value => value.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
            Assert.Equal(0, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
            Assert.Equal("GET", Assert.Single(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods));
        }
        Assert.DoesNotContain(endpoints, value => value.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "CompleteBranchCashSale");
        Assert.Equal(400, (await fixture.Call("GetCashHistoryDetail", routeValue: " bad ", principal: LifecyclePrincipal())).Status);
        Assert.Equal(404, (await fixture.Call("GetCashHistoryDetail", routeValue: Guid.NewGuid().ToString(), principal: LifecyclePrincipal())).Status);
    }
}
