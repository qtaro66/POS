using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    private static readonly DateTimeOffset OverviewTime = DateTimeOffset.UtcNow.AddMinutes(-5);
    private static string OverviewStamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static async Task OverviewEvent(Fixture fixture, string branch = "BRANCH-01", bool delivered = false)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture.Path, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cloud_sync_outbox (outbox_id, branch_id, branch_sequence, request_id, table_session_id,
                action, table_version, created_at, processing_status, successfully_sent_at, last_error)
            VALUES ($id,$branch,1,'SYNTHETIC-REQUEST','SYNTHETIC-TABLE','SubmitOrder',1,$created,$status,$ack,'PRIVATE-SERVER-ERROR');
            """;
        command.Parameters.AddWithValue("$id", "OVERVIEW-EVENT-" + branch);
        command.Parameters.AddWithValue("$branch", branch);
        command.Parameters.AddWithValue("$created", OverviewStamp(OverviewTime.AddMinutes(-1)));
        command.Parameters.AddWithValue("$status", delivered ? "Completed" : "Pending");
        command.Parameters.AddWithValue("$ack", delivered ? OverviewStamp(OverviewTime.AddSeconds(1)) : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task OverviewReplay(Fixture fixture, string branch = "BRANCH-01", bool delivered = false)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture.Path, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cloud_sync_sequence_replay_requests (recovery_id, branch_id, expected_branch_sequence,
                received_branch_sequence, expected_outbox_id, requested_at, completed_at)
            VALUES ($id,$branch,1,2,$event,$requested,$ack);
            """;
        command.Parameters.AddWithValue("$id", "OVERVIEW-REPLAY-" + branch);
        command.Parameters.AddWithValue("$branch", branch);
        command.Parameters.AddWithValue("$event", "OVERVIEW-EVENT-" + branch);
        command.Parameters.AddWithValue("$requested", OverviewStamp(OverviewTime));
        command.Parameters.AddWithValue("$ack", delivered ? OverviewStamp(OverviewTime.AddSeconds(3)) : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> OverviewReport(Fixture fixture, string branch = "BRANCH-01", bool delivered = false)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(OverviewTime, "Asia/Bangkok").DateTime);
        var open = new StoreLifecycleCommand(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
            StoreLifecycleAction.OpenStore, 500, new("OVERVIEW-EMPLOYEE", "OVERVIEW-DEVICE", OverviewTime.AddSeconds(-30), day));
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        await store.ExecuteStoreLifecycleAsync(branch, open);
        var close = open with { RequestId = Guid.NewGuid().ToString(), Action = StoreLifecycleAction.CloseStore, CashAmount = 490 };
        await store.ExecuteStoreLifecycleAsync(branch, close);
        if (delivered)
        {
            var at = OverviewTime.AddSeconds(2);
            var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync(branch, "PRIVATE-REPORT-WORKER", at, TimeSpan.FromMinutes(1)));
            Assert.True(await store.ConfirmClosingReportAsync(lease, at));
        }
        return close.RequestId;
    }

    public static IEnumerable<object[]> DeliveryOverviewMatrix()
    {
        // 0 = absent, 1 = pending, 2 = acknowledged. Replay requires a real FK anchor event.
        foreach (var table in Enumerable.Range(0, 3))
            foreach (var report in Enumerable.Range(0, 3))
                foreach (var replay in Enumerable.Range(0, 3))
                    yield return [table, report, replay];
    }

    [Theory]
    [MemberData(nameof(DeliveryOverviewMatrix))]
    public async Task Delivery_overview_matrix_never_claims_current_while_any_source_is_pending(int table, int report, int replay)
    {
        await using var fixture = await Fixture.CreateAsync();
        var effectiveTable = table == 0 && replay > 0 ? 2 : table;
        if (effectiveTable > 0) { await OverviewEvent(fixture, delivered: effectiveTable == 2); }
        if (report > 0) { await OverviewReport(fixture, delivered: report == 2); }
        if (replay > 0) { await OverviewReplay(fixture, delivered: replay == 2); }
        var response = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        Assert.Equal(200, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        Assert.Equal(1, response.Body.GetProperty("schemaVersion").GetInt32());
        Assert.False(response.Body.GetProperty("tableDeliveryEnabled").GetBoolean());
        Assert.False(response.Body.GetProperty("reportDeliveryEnabled").GetBoolean());
        var snapshot = response.Body.GetProperty("snapshot");
        var pending = effectiveTable == 1 || report == 1 || replay == 1;
        var history = effectiveTable == 2 || report == 2 || replay == 2;
        Assert.Equal(pending ? "Pending" : history ? "Current" : "NoHistory", snapshot.GetProperty("state").GetString());
        Assert.Equal("BRANCH-01", snapshot.GetProperty("branchId").GetString());
        AssertChannel(snapshot.GetProperty("tableEvents"), effectiveTable);
        AssertChannel(snapshot.GetProperty("closingReports"), report);
        Assert.Equal(replay == 1 ? 1 : 0, snapshot.GetProperty("replayPendingCount").GetInt64());
        Assert.Equal(replay == 2 ? 1 : 0, snapshot.GetProperty("replayCompletedCount").GetInt64());
        var expectedLast = replay == 2 ? OverviewTime.AddSeconds(3) : report == 2 ? OverviewTime.AddSeconds(2) : effectiveTable == 2 ? OverviewTime.AddSeconds(1) : (DateTimeOffset?)null;
        Assert.Equal(expectedLast, snapshot.GetProperty("lastConfirmedAt").Deserialize<DateTimeOffset?>());
        Assert.DoesNotContain("PRIVATE", response.Body.GetRawText());
    }

    private static void AssertChannel(JsonElement channel, int state)
    {
        Assert.Equal(state == 0 ? 0 : 1, channel.GetProperty("totalCount").GetInt64());
        Assert.Equal(state == 1 ? 1 : 0, channel.GetProperty("pendingCount").GetInt64());
        Assert.Equal(state == 2 ? 1 : 0, channel.GetProperty("deliveredCount").GetInt64());
        Assert.Equal(state == 1 ? JsonValueKind.String : JsonValueKind.Null, channel.GetProperty("oldestPendingAt").ValueKind);
        Assert.Equal(state == 2 ? JsonValueKind.String : JsonValueKind.Null, channel.GetProperty("lastConfirmedAt").ValueKind);
    }

    [Fact]
    public async Task Delivery_overview_requires_complete_pc_identity_before_reading_storage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incomplete = LifecyclePrincipal();
        var identity = (ClaimsIdentity)incomplete.Identity!;
        identity.RemoveClaim(identity.Claims.Single(claim => claim.Value == "EMP-1"));
        foreach (var principal in new[] { LifecyclePrincipal("Mobile"), LifecyclePrincipal("Kitchen"), incomplete, new ClaimsPrincipal() })
        {
            var response = await fixture.Call("GetDeliveryOverview", principal: principal);
            Assert.Equal(403, response.Status);
            Assert.Equal("no-store", response.CacheControl);
        }
    }

    [Theory]
    [InlineData("?branchId=OTHER")]
    [InlineData("?status=Current")]
    [InlineData("?branchId=BRANCH-01&branchId=OTHER")]
    public async Task Delivery_overview_rejects_query_scope_or_status_injection(string query)
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal(), query: query);
        Assert.Equal(400, response.Status);
        Assert.Equal("InvalidDeliveryOverviewQuery", response.Body.GetProperty("error").GetString());
        Assert.Equal("no-store", response.CacheControl);
    }

    [Fact]
    public async Task Delivery_overview_is_branch_scoped_and_observation_does_not_mutate_queues()
    {
        await using var fixture = await Fixture.CreateAsync();
        await OverviewEvent(fixture);
        await OverviewReport(fixture);
        await OverviewEvent(fixture, "OTHER", delivered: true);
        await OverviewReport(fixture, "OTHER", delivered: true);
        await OverviewReplay(fixture, "OTHER", delivered: true);
        var before = await OverviewFingerprint(fixture);
        var own = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        var current = own.Body.GetProperty("snapshot");
        Assert.Equal("Pending", current.GetProperty("state").GetString());
        Assert.Null(current.GetProperty("lastConfirmedAt").Deserialize<DateTimeOffset?>());
        AssertChannel(current.GetProperty("tableEvents"), 1);
        AssertChannel(current.GetProperty("closingReports"), 1);
        Assert.Equal(0, current.GetProperty("replayCompletedCount").GetInt64());
        var other = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal(branch: "OTHER"));
        Assert.Equal("Current", other.Body.GetProperty("snapshot").GetProperty("state").GetString());
        var empty = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal(branch: "EMPTY"));
        Assert.Equal("NoHistory", empty.Body.GetProperty("snapshot").GetProperty("state").GetString());
        Assert.Equal(before, await OverviewFingerprint(fixture));
    }

    private static async Task<string> OverviewFingerprint(Fixture fixture) =>
        string.Join("|", await fixture.Scalar("SELECT json_group_array(json_object('id',outbox_id,'status',processing_status,'retry',retry_count,'ack',successfully_sent_at,'error',last_error)) FROM cloud_sync_outbox;"),
            await fixture.Scalar("SELECT json_group_array(json_object('id',request_id,'payload',payload_json,'attempt',attempt_count,'lease',lease_id,'owner',lease_owner,'ack',delivered_at,'failure',last_failure)) FROM closing_report_outbox;"),
            await fixture.Scalar("SELECT json_group_array(json_object('id',recovery_id,'complete',completed_at)) FROM cloud_sync_sequence_replay_requests;"));

    [Theory]
    [InlineData("cloud_sync_outbox")]
    [InlineData("closing_report_outbox")]
    [InlineData("cloud_sync_sequence_replay_requests")]
    public async Task Delivery_overview_missing_any_source_is_unavailable_not_partial_success(string table)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Execute($"DROP TABLE {table};");
        var response = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        Assert.Equal(503, response.Status);
        Assert.Equal("no-store", response.CacheControl);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("report")]
    [InlineData("replay")]
    public async Task Delivery_overview_corrupt_timestamp_in_any_source_is_unavailable_without_error_disclosure(string source)
    {
        await using var fixture = await Fixture.CreateAsync();
        await OverviewEvent(fixture, delivered: true);
        await OverviewReport(fixture, delivered: true);
        await OverviewReplay(fixture, delivered: true);
        await fixture.Execute(source switch
        {
            "table" => "UPDATE cloud_sync_outbox SET successfully_sent_at='PRIVATE-INVALID-TIMESTAMP';",
            "report" => "UPDATE closing_report_outbox SET delivered_at='PRIVATE-INVALID-TIMESTAMP';",
            _ => "UPDATE cloud_sync_sequence_replay_requests SET completed_at='PRIVATE-INVALID-TIMESTAMP';"
        });
        var response = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        Assert.Equal(503, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        Assert.True(response.Body.ValueKind == JsonValueKind.Undefined || !response.Body.GetRawText().Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delivery_overview_new_close_and_lost_acknowledgement_stay_pending_until_report_confirmation_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await OverviewEvent(fixture, delivered: true);
        Assert.Equal("Current", (await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal())).Body.GetProperty("snapshot").GetProperty("state").GetString());
        var request = await OverviewReport(fixture);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("BRANCH-01", "PRIVATE-WORKER", now, TimeSpan.FromMinutes(1)));
        Assert.Equal(request, lease.RequestId);
        var inFlight = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        Assert.Equal("Pending", inFlight.Body.GetProperty("snapshot").GetProperty("state").GetString());
        Assert.Equal(1, inFlight.Body.GetProperty("snapshot").GetProperty("closingReports").GetProperty("inFlightCount").GetInt64());
        Assert.DoesNotContain(lease.LeaseId, inFlight.Body.GetRawText());
        Assert.DoesNotContain("PRIVATE-WORKER", inFlight.Body.GetRawText());
        Assert.True(await store.RetryClosingReportAsync(lease, now, TimeSpan.FromHours(1), ClosingReportDeliveryFailure.AcknowledgmentUnavailable));
        var lost = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        Assert.Equal("Pending", lost.Body.GetProperty("snapshot").GetProperty("state").GetString());
        Assert.Equal(1, lost.Body.GetProperty("snapshot").GetProperty("closingReports").GetProperty("delayedCount").GetInt64());
        Assert.Equal(OverviewTime.AddSeconds(1), lost.Body.GetProperty("snapshot").GetProperty("lastConfirmedAt").GetDateTimeOffset());
        var reopened = new SqliteTableSessionCommandStore(fixture.Path);
        await fixture.Execute("UPDATE closing_report_outbox SET available_at='2026-01-01T00:00:00Z';");
        var replayLease = Assert.IsType<ClosingReportLease>(await reopened.ClaimClosingReportAsync("BRANCH-01", "PRIVATE-RESTART", now, TimeSpan.FromMinutes(1)));
        Assert.Equal(lease.ReportId, replayLease.ReportId);
        Assert.Equal(lease.PayloadJson, replayLease.PayloadJson);
        Assert.True(await reopened.ConfirmClosingReportAsync(replayLease, now));
        var confirmed = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        Assert.Equal("Current", confirmed.Body.GetProperty("snapshot").GetProperty("state").GetString());
        Assert.Equal(0, confirmed.Body.GetProperty("snapshot").GetProperty("closingReports").GetProperty("pendingCount").GetInt64());
        Assert.Equal(now, confirmed.Body.GetProperty("snapshot").GetProperty("lastConfirmedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Delivery_overview_unknown_statuses_remain_unconfirmed_and_do_not_expose_raw_values()
    {
        await using var fixture = await Fixture.CreateAsync();
        await OverviewEvent(fixture);
        await OverviewReport(fixture);
        await fixture.Execute("UPDATE cloud_sync_outbox SET processing_status='PRIVATE-UNKNOWN-STATUS'; UPDATE closing_report_outbox SET last_failure='PRIVATE-UNKNOWN-FAILURE';");
        var result = await fixture.Call("GetDeliveryOverview", principal: LifecyclePrincipal());
        var snapshot = result.Body.GetProperty("snapshot");
        Assert.Equal("Pending", snapshot.GetProperty("state").GetString());
        Assert.Equal(1, snapshot.GetProperty("tableEvents").GetProperty("unknownCount").GetInt64());
        Assert.Equal(1, snapshot.GetProperty("closingReports").GetProperty("unknownCount").GetInt64());
        Assert.DoesNotContain("PRIVATE", result.Body.GetRawText());
    }

    [Theory]
    [InlineData(true, true, "BRANCH-01", "same", true, true)]
    [InlineData(false, false, "BRANCH-01", "same", false, false)]
    [InlineData(true, true, "OTHER", "same", false, false)]
    [InlineData(true, true, "BRANCH-01", "other", false, true)]
    [InlineData(true, true, "BRANCH-01", "relative", false, true)]
    [InlineData(true, false, "BRANCH-01", "canonical", true, false)]
    public async Task Delivery_overview_worker_configuration_is_scoped_to_branch_and_ordering_database(bool tablesEnabled,
        bool reportsEnabled, string branch, string pathKind, bool expectedTables, bool expectedReports)
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = pathKind switch
        {
            "same" => fixture.Path,
            "canonical" => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, ".", "branch.db"),
            "relative" => "PRIVATE-RELATIVE.db",
            _ => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, "PRIVATE-OTHER.db")
        };
        var context = new DefaultHttpContext();
        var result = await StoreLifecycleEndpoints.DeliveryOverviewAsync(LifecyclePrincipal(), new BranchOrderingRuntime(fixture.Path, TimeProvider.System),
            context, default,
            Options.Create(new BranchCloudOutboxDeliveryOptions { Enabled = tablesEnabled, BranchId = branch, DatabasePath = path, ServiceOrigin = "https://private.example" }),
            Options.Create(new ClosingReportDeliveryOptions { Enabled = reportsEnabled, BranchId = branch, CentralOrigin = "https://private-report.example" }));
        var value = Assert.IsType<Ok<StoreLifecycleEndpoints.DeliveryOverviewResponse>>(result).Value!;
        Assert.Equal(expectedTables, value.TableDeliveryEnabled);
        Assert.Equal(expectedReports, value.ReportDeliveryEnabled);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        var json = JsonSerializer.SerializeToElement(value);
        Assert.Equal(4, json.EnumerateObject().Count());
        Assert.DoesNotContain("PRIVATE", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Path, json.GetRawText());
        Assert.DoesNotContain("origin", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delivery_overview_cancelled_request_propagates_without_mutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await OverviewReport(fixture);
        var before = await OverviewFingerprint(fixture);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StoreLifecycleEndpoints.DeliveryOverviewAsync(LifecyclePrincipal(),
            new BranchOrderingRuntime(fixture.Path, TimeProvider.System), context, cancellation.Token));
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(before, await OverviewFingerprint(fixture));
    }
}
