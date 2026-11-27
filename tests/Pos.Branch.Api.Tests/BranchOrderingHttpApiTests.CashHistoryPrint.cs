using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Branch.Api.Ordering;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private const string PrintEvidence = "GetCashHistoryPrintEvidence";
    private const string PrintAttempts = "GetCashHistoryPrintAttempts";
    private static async Task<(string Snapshot, string Job)> PrintSubject(Fixture fixture)
    {
        await CapturedItemHistory(fixture);
        return ((await fixture.Scalar("SELECT snapshot_id FROM production_order_snapshots LIMIT 1;"))!,
            (await fixture.Scalar("SELECT print_job_id FROM print_jobs WHERE purpose='KitchenOrder' LIMIT 1;"))!);
    }
    private static async Task<string?> PrintHistoryFingerprint(Fixture fixture) => await ProductionFingerprint(fixture) + ":" +
        await fixture.Scalar("""
            SELECT coalesce((SELECT group_concat(print_job_id || status || attempt_count || coalesce(lease_owner,'')) FROM print_jobs),'') || ':' ||
                coalesce((SELECT group_concat(print_attempt_id || status || automatic_retry_allowed || coalesce(error_code,'')) FROM print_attempts),'') || ':' ||
                (SELECT count(*) FROM rendered_print_payloads) || ':' || (SELECT schema_version FROM pragma_schema_version);
            """);

    private static Task SeedObservedAttempt(Fixture fixture, string job, bool invalidRetry = false) => fixture.Execute($$"""
        INSERT INTO printer_profiles (
            printer_id,branch_id,display_name,production_station_id,host,port,protocol,compatibility_profile_id,
            manufacturer,model,product_code,firmware_version,paper_width_millimeters,dpi,printable_width_dots,
            render_mode,thai_encoding_mode,status_mode,supports_cash_drawer,cutter_command_hex,
            drawer_pulse_command_hex,status_command_profile,connect_timeout_ms,send_timeout_ms,
            maximum_automatic_retries_before_send,automatic_retry_backoff_ms,is_active,updated_at)
        VALUES('PRIVATE-PRINTER','BRANCH-01','Private fixture','KITCHEN-1','private-printer.invalid',9100,'RawTcpEscPos',
            'epson-tm-t82x-m352a-v1','Epson','TM-T82X',NULL,NULL,80,203,576,'BitmapRaster','UnicodeRaster','Unsupported',0,
            '1D5601',NULL,NULL,2000,10000,3,1000,1,'2026-11-27T01:00:00Z');
        UPDATE print_jobs SET status='Unknown',attempt_count=1 WHERE print_job_id='{{job}}';
        INSERT INTO print_attempts(print_attempt_id,print_job_id,printer_id,attempt_number,worker_id,payload_sha256,
            status,automatic_retry_allowed,error_code,started_at,first_byte_at,finished_at)
        VALUES('ATTEMPT-1','{{job}}','PRIVATE-PRINTER',1,'PRIVATE-WORKER','{{new string('A', 64)}}','Unknown',
            {{(invalidRetry ? 1 : 0)}},'PRIVATE-ERROR','2026-11-27T01:00:00Z','2026-11-27T01:00:01Z','2026-11-27T01:00:02Z');
        """);

    [Fact]
    public async Task Cash_history_print_reads_actual_submitted_job_and_observed_unknown_attempt_without_writes()
    {
        await using var fixture = await Fixture.CreateAsync(); var subject = await PrintSubject(fixture);
        var initial = await fixture.Call(PrintEvidence, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot, principal: LifecyclePrincipal());
        Assert.Equal(200, initial.Status); Assert.Equal("no-store", initial.CacheControl);
        var original = initial.Body.GetProperty("job"); Assert.Equal(subject.Job, original.GetProperty("printJobId").GetString());
        Assert.Equal("ORDER-ITEMS", original.GetProperty("requestId").GetString());
        Assert.Equal("PrintQueued", original.GetProperty("status").GetString());
        Assert.False(original.GetProperty("isReprint").GetBoolean());
        var empty = await fixture.Call(PrintAttempts, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot,
            printJobId: subject.Job, principal: LifecyclePrincipal());
        Assert.Equal(200, empty.Status); Assert.Empty(empty.Body.GetProperty("items").EnumerateArray());
        await SeedObservedAttempt(fixture, subject.Job);
        var before = await PrintHistoryFingerprint(fixture);
        var page = await fixture.Call(PrintAttempts, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot,
            printJobId: subject.Job, principal: LifecyclePrincipal());
        Assert.Equal(200, page.Status); Assert.Equal("no-store", page.CacheControl);
        Assert.Equal("Unknown", page.Body.GetProperty("job").GetProperty("status").GetString());
        var attempt = Assert.Single(page.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("Unknown", attempt.GetProperty("status").GetString());
        Assert.False(attempt.GetProperty("automaticRetryAllowed").GetBoolean());
        Assert.DoesNotContain("PRIVATE", page.Body.GetRawText());
        Assert.DoesNotContain("payload", page.Body.GetRawText());
        Assert.DoesNotContain("lease", page.Body.GetRawText());
        Assert.Equal(before, await PrintHistoryFingerprint(fixture));
    }

    [Theory]
    [InlineData(PrintEvidence)]
    [InlineData(PrintAttempts)]
    public async Task Cash_history_print_requires_complete_pc_identity_and_rejects_foreign_parent_or_snapshot(string endpoint)
    {
        await using var fixture = await Fixture.CreateAsync(); var subject = await PrintSubject(fixture);
        var incomplete = LifecyclePrincipal(); var identity = (ClaimsIdentity)incomplete.Identity!;
        identity.RemoveClaim(identity.Claims.Single(claim => claim.Value == "EMP-1"));
        var before = await PrintHistoryFingerprint(fixture);
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), incomplete, new ClaimsPrincipal() })
        {
            var denied = await fixture.Call(endpoint, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot,
                printJobId: subject.Job, principal: principal);
            Assert.Equal(403, denied.Status); Assert.Equal("no-store", denied.CacheControl);
        }
        var foreign = await fixture.Call(endpoint, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot,
            printJobId: subject.Job, principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal(404, foreign.Status); Assert.Equal("CashHistoryNotFound", foreign.Body.GetProperty("error").GetString());
        Assert.Equal(before, await PrintHistoryFingerprint(fixture));
        var other = await SavedCashHistory(fixture);
        var beforeMixed = await PrintHistoryFingerprint(fixture);
        var mixed = await fixture.Call(endpoint, routeValue: other.Request, snapshotId: subject.Snapshot,
            printJobId: subject.Job, principal: LifecyclePrincipal());
        Assert.Equal(404, mixed.Status); Assert.Equal("CashHistoryProductionSnapshotNotFound", mixed.Body.GetProperty("error").GetString());
        Assert.Equal(beforeMixed, await PrintHistoryFingerprint(fixture));
    }

    [Theory]
    [InlineData(PrintEvidence, "?limit=1")]
    [InlineData(PrintEvidence, "?branchId=OTHER")]
    [InlineData(PrintAttempts, "?limit=0")]
    [InlineData(PrintAttempts, "?limit=51")]
    [InlineData(PrintAttempts, "?limit=1&limit=2")]
    [InlineData(PrintAttempts, "?Limit=1")]
    [InlineData(PrintAttempts, "?cursor=bad")]
    [InlineData(PrintAttempts, "?source=OTHER")]
    public async Task Cash_history_print_rejects_ambiguous_query_overrides(string endpoint, string query)
    {
        await using var fixture = await Fixture.CreateAsync(); var subject = await PrintSubject(fixture);
        var response = await fixture.Call(endpoint, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot,
            printJobId: subject.Job, principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal("InvalidCashHistoryPrintQuery", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Cash_history_print_distinguishes_missing_original_job_from_missing_attempt_subject()
    {
        await using var fixture = await Fixture.CreateAsync(); var subject = await PrintSubject(fixture);
        var missing = await fixture.Call(PrintAttempts, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot,
            printJobId: "MISSING-JOB", principal: LifecyclePrincipal());
        Assert.Equal(404, missing.Status); Assert.Equal("CashHistoryPrintJobNotFound", missing.Body.GetProperty("error").GetString());
        await fixture.Execute($"DELETE FROM print_jobs WHERE print_job_id='{subject.Job}';");
        var before = await PrintHistoryFingerprint(fixture);
        var evidence = await fixture.Call(PrintEvidence, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot, principal: LifecyclePrincipal());
        Assert.Equal(200, evidence.Status); Assert.Equal(JsonValueKind.Null, evidence.Body.GetProperty("job").ValueKind);
        Assert.Equal(before, await PrintHistoryFingerprint(fixture));
    }

    [Theory]
    [InlineData(PrintEvidence)]
    [InlineData(PrintAttempts)]
    public async Task Cash_history_print_rejects_noncanonical_or_oversized_path_identities(string endpoint)
    {
        await using var fixture = await Fixture.CreateAsync(); var subject = await PrintSubject(fixture);
        var before = await PrintHistoryFingerprint(fixture);
        var subjects = new List<(string Request, string Snapshot, string Job)>
        {
            (" CASH-ITEMS", subject.Snapshot, subject.Job),
            ("CASH-ITEMS", new string('x', 1025), subject.Job),
            ("CASH-ITEMS", "bad\u0085snapshot", subject.Job)
        };
        if (endpoint == PrintAttempts) { subjects.Add(("CASH-ITEMS", subject.Snapshot, new string('x', 1025))); }
        foreach (var item in subjects)
        {
            var response = await fixture.Call(endpoint, routeValue: item.Request, snapshotId: item.Snapshot,
                printJobId: item.Job, principal: LifecyclePrincipal());
            Assert.Equal(400, response.Status); Assert.Equal("no-store", response.CacheControl);
            Assert.Equal("InvalidCashHistoryIdentity", response.Body.GetProperty("error").GetString());
        }
        Assert.Equal(before, await PrintHistoryFingerprint(fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cash_history_print_corrupt_source_is_unavailable_without_disclosure_or_repair(bool badAttempt)
    {
        await using var fixture = await Fixture.CreateAsync(); var subject = await PrintSubject(fixture);
        if (badAttempt) { await SeedObservedAttempt(fixture, subject.Job, true); }
        else { await fixture.Execute("UPDATE production_order_snapshots SET content_sha256=printf('%064d',0);"); }
        var before = await PrintHistoryFingerprint(fixture);
        var response = await fixture.Call(badAttempt ? PrintAttempts : PrintEvidence, routeValue: "CASH-ITEMS", snapshotId: subject.Snapshot,
            printJobId: subject.Job, principal: LifecyclePrincipal());
        Assert.Equal(503, response.Status); Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(JsonValueKind.Undefined, response.Body.ValueKind);
        Assert.Equal(before, await PrintHistoryFingerprint(fixture));
    }

    [Theory]
    [InlineData(PrintEvidence)]
    [InlineData(PrintAttempts)]
    public async Task Cash_history_print_routes_accept_no_body_and_are_independent_of_cash_command_flag(string name)
    {
        await using var fixture = await Fixture.CreateAsync(); var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new BranchOrderingRuntime(fixture.Path, TimeProvider.System));
        await using var app = builder.Build();
        app.MapBranchOrderingIfEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BranchOrdering:Enabled"] = "true", ["BranchOrdering:EnableCashSaleTest"] = "false" }).Build());
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
        var endpoint = Assert.Single(endpoints, value => value.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
        Assert.Equal(0, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
        Assert.Equal("GET", Assert.Single(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods));
        Assert.DoesNotContain(endpoints, value => value.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "CompleteBranchCashSale");
    }
}
