using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudReplayStatusHttpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Query_is_read_only_and_preserves_exact_int64_fields_under_authenticated_scope()
    {
        await using var fixture = new Fixture();
        var result = await fixture.Call("?afterSequence=9007199254740993", branch: "BRANCH-02");
        Assert.Equal(200, result.Status); Assert.Contains("no-store", result.Cache);
        Assert.Equal("BRANCH-02", fixture.Query.Branch); Assert.Equal(50, fixture.Query.Limit);
        Assert.Equal(9007199254740993L, fixture.Query.After);
        var item = result.Json.GetProperty("items")[0];
        Assert.Equal((long.MaxValue - 1).ToString(CultureInfo.InvariantCulture), item.GetProperty("expectedBranchSequence").GetString());
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), item.GetProperty("receivedBranchSequence").GetString());
        Assert.Equal("9007199254740993", item.GetProperty("lastExpectedBranchSequence").GetString());
    }

    [Theory]
    [InlineData("?afterSequence=")]
    [InlineData("?afterSequence=0")]
    [InlineData("?afterSequence=-1")]
    [InlineData("?afterSequence=01")]
    [InlineData("?afterSequence=1.0")]
    [InlineData("?afterSequence=9223372036854775808")]
    [InlineData("?afterSequence=1&afterSequence=2")]
    [InlineData("?afterSequence=%D9%A1")]
    [InlineData("?branchId=OTHER")]
    [InlineData("?limit=100000")]
    public async Task Invalid_parameters_never_reach_the_query(string query)
    {
        await using var fixture = new Fixture();
        Assert.Equal(400, (await fixture.Call(query)).Status); Assert.Equal(0, fixture.Query.Calls);
    }

    [Fact]
    public async Task Identity_https_body_and_feature_guards_prevent_reads()
    {
        await using var fixture = new Fixture();
        Assert.Equal(401, (await fixture.Call(authenticated: false)).Status);
        Assert.Equal(400, (await fixture.Call(https: false)).Status);
        Assert.Equal(413, (await fixture.Call(body: true)).Status);
        Assert.Equal(413, (await fixture.Call(body: true, unknownLength: true)).Status);
        Assert.Equal(0, fixture.Query.Calls);
        await using var disabled = new Fixture(enabled: false); Assert.Empty(disabled.Endpoints);
    }

    [Fact]
    public async Task Failures_are_redacted_and_request_cancellation_propagates()
    {
        await using var fixture = new Fixture();
        foreach (var error in new Exception[] { new SqliteException("PRIVATE path", 1), new FormatException("PRIVATE timestamp"), new OperationCanceledException("PRIVATE timeout") })
        {
            fixture.Query.Error = error;
            var result = await fixture.Call();
            Assert.Equal(503, result.Status); Assert.Equal("CloudReplayStatusUnavailable", result.Json.GetProperty("code").GetString());
            Assert.DoesNotContain("PRIVATE", result.Json.GetRawText());
        }
        fixture.Query.Error = null; using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Call(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Query_cannot_create_a_missing_database_and_validates_input_before_io()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-replay-" + Guid.NewGuid().ToString("N") + ".db");
        var query = new SqliteBranchCloudReplayStatusQuery(path);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.ListAsync("BRANCH-01", 201));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.ListAsync("BRANCH-01", 50, 0));
        await Assert.ThrowsAsync<SqliteException>(() => query.ListAsync("BRANCH-01", 50)); Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Real_sqlite_keeps_acknowledged_events_visible_for_replay_without_mutating_any_evidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-replay-status-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "branch.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                for (var index = 1; index <= 53; index++)
                {
                    using var seed = connection.CreateCommand();
                    seed.CommandText = (index == 52 ? "" : """
                        INSERT INTO cloud_sync_outbox (outbox_id, branch_id, branch_sequence, request_id, table_session_id, action, table_version,
                            created_at, processing_status, successfully_sent_at, last_error)
                        VALUES ($event, $branch, $sequence, 'REQUEST-1', 'SESSION-1', 'SubmitOrder', 1, $created, 'Completed', $now, 'PRIVATE error');
                        """) + """
                        INSERT INTO cloud_sync_sequence_replay_requests (recovery_id, branch_id, expected_branch_sequence,
                            received_branch_sequence, expected_outbox_id, requested_at, completed_at)
                        VALUES ($recovery, $branch, $sequence, $received, $event, $created, $completed);
                        """;
                    seed.Parameters.AddWithValue("$event", index == 52 ? "EVENT-1" : "EVENT-" + index.ToString(CultureInfo.InvariantCulture));
                    seed.Parameters.AddWithValue("$recovery", "RECOVERY-" + index.ToString(CultureInfo.InvariantCulture));
                    seed.Parameters.AddWithValue("$branch", index == 53 ? "BRANCH-02" : "BRANCH-01");
                    seed.Parameters.AddWithValue("$sequence", index == 53 ? long.MaxValue - 1 : index == 52 ? 1 : index);
                    seed.Parameters.AddWithValue("$received", index == 53 ? long.MaxValue : 54);
                    seed.Parameters.AddWithValue("$created", Stamp(Now.AddMinutes(-3))); seed.Parameters.AddWithValue("$now", Stamp(Now));
                    seed.Parameters.AddWithValue("$completed", index == 52 ? Stamp(Now) : DBNull.Value);
                    await seed.ExecuteNonQueryAsync();
                }
                using (var leases = connection.CreateCommand())
                {
                    leases.CommandText = """
                        INSERT INTO cloud_sync_sequence_replay_leases (recovery_id, retry_count, lease_owner, claimed_at, lease_expires_at)
                        VALUES ('RECOVERY-2', 0, 'PRIVATE owner', $claimed, $expired);
                        INSERT INTO cloud_sync_sequence_replay_leases (recovery_id, retry_count, next_retry_at)
                        VALUES ('RECOVERY-3', 2, $next);
                        """;
                    leases.Parameters.AddWithValue("$claimed", Stamp(Now.AddMinutes(-2))); leases.Parameters.AddWithValue("$expired", Stamp(Now.AddMinutes(-1)));
                    leases.Parameters.AddWithValue("$next", Stamp(Now.AddMinutes(2))); await leases.ExecuteNonQueryAsync();
                }
                foreach (var newer in new[] { false, true })
                {
                    using var failure = connection.CreateCommand();
                    failure.CommandText = """
                        INSERT INTO cloud_sync_sequence_replay_failures (failure_id, recovery_id, branch_id, outbox_id, branch_sequence,
                            lease_owner, claimed_at, lease_expires_at, failed_at, next_retry_at, error_code, retry_count_after, expected_branch_sequence)
                        VALUES ($id, 'RECOVERY-3', 'BRANCH-01', 'EVENT-3', 3, 'PRIVATE owner', $claim, $lease, $failed, $next, 'PRIVATE diagnostic', 2, $expected);
                        """;
                    failure.Parameters.AddWithValue("$id", newer ? "A-NEW" : "Z-OLD"); failure.Parameters.AddWithValue("$claim", Stamp(Now.AddMinutes(-1)));
                    failure.Parameters.AddWithValue("$lease", Stamp(Now.AddMinutes(1))); failure.Parameters.AddWithValue("$failed", Stamp(newer ? Now : Now.AddTicks(-1)));
                    failure.Parameters.AddWithValue("$next", Stamp(Now.AddMinutes(2))); failure.Parameters.AddWithValue("$expected", newer ? 1 : 2);
                    await failure.ExecuteNonQueryAsync();
                }
            }
            var before = await File.ReadAllBytesAsync(path);
            await using (var fixture = new Fixture(reader: new SqliteBranchCloudReplayStatusQuery(path)))
            {
                var first = await fixture.Call(); Assert.Equal(200, first.Status);
                var summary = first.Json.GetProperty("summary");
                Assert.Equal(51, summary.GetProperty("pendingCount").GetInt64()); Assert.Equal(1, summary.GetProperty("leasedCount").GetInt64());
                Assert.Equal(50, summary.GetProperty("unleasedCount").GetInt64()); Assert.Equal(1, summary.GetProperty("withRetriesCount").GetInt64());
                Assert.Equal(50, first.Json.GetProperty("items").GetArrayLength());
                Assert.False(first.Json.GetProperty("items")[0].GetProperty("hasLease").GetBoolean());
                Assert.True(first.Json.GetProperty("items")[1].GetProperty("hasLease").GetBoolean());
                Assert.Equal(Now.AddMinutes(-1), first.Json.GetProperty("items")[1].GetProperty("leaseExpiresAt").GetDateTimeOffset());
                Assert.Equal("1", first.Json.GetProperty("items")[2].GetProperty("lastExpectedBranchSequence").GetString());
                Assert.Equal(Now, first.Json.GetProperty("items")[2].GetProperty("lastFailedAt").GetDateTimeOffset());
                var second = await fixture.Call("?afterSequence=" + first.Json.GetProperty("nextAfterSequence").GetString());
                Assert.Equal(1, second.Json.GetProperty("items").GetArrayLength()); Assert.Null(second.Json.GetProperty("nextAfterSequence").GetString());
                Assert.Equal("RECOVERY-51", second.Json.GetProperty("items")[0].GetProperty("recoveryId").GetString());
                Assert.DoesNotContain("PRIVATE", first.Json.GetRawText() + second.Json.GetRawText());
                var other = await fixture.Call(branch: "BRANCH-02"); Assert.Equal(1, other.Json.GetProperty("summary").GetProperty("pendingCount").GetInt64());
                Assert.Equal((long.MaxValue - 1).ToString(CultureInfo.InvariantCulture), other.Json.GetProperty("items")[0].GetProperty("expectedBranchSequence").GetString());
                Assert.Equal(0, (await new SqliteBranchCloudOutboxStatusQuery(path).ListAsync("BRANCH-01", 50)).Summary.UnacknowledgedCount);
                Assert.Equal(before, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) { File.Delete(file); }
            if (Directory.Exists(directory)) { Directory.Delete(directory); }
        }
    }
    private static string Stamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class QueryStub : IBranchCloudReplayStatusQuery
    {
        public string? Branch { get; private set; }
        public int Limit { get; private set; }
        public long? After { get; private set; }
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public Task<BranchCloudReplayStatusPage> ListAsync(string branchId, int maximumResults, long? afterSequence = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; Branch = branchId; Limit = maximumResults; After = afterSequence;
            if (Error is not null) { throw Error; }
            return Task.FromResult(new BranchCloudReplayStatusPage(new(1, 0, 1, 1), [new("RECOVERY-1", "EVENT-1", long.MaxValue - 1,
                long.MaxValue, "REQUEST-1", "SESSION-1", "SubmitOrder", Now, false, 2, null, null, Now, 9007199254740993L)], null));
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly IConfiguration _configuration;
        public QueryStub Query { get; } = new();
        public IEnumerable<Endpoint> Endpoints => ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints);
        public Fixture(bool enabled = true, IBranchCloudReplayStatusQuery? reader = null)
        {
            _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BranchOrdering:Enabled"] = enabled.ToString() }).Build();
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(new BranchOrderingRuntime(Path.Combine(Path.GetTempPath(), "unused-replay-status-test.db"), new Clock(), replayStatusQuery: reader ?? Query));
            _app = builder.Build(); _app.MapBranchOrderingIfEnabled(_configuration);
        }
        public async Task<(int Status, JsonElement Json, string Cache)> Call(string query = "", string branch = "BRANCH-01", bool authenticated = true, bool https = true, bool body = false, bool unknownLength = false, CancellationToken cancellationToken = default)
        {
            var endpoint = Endpoints.Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetBranchCloudReplayStatus");
            Assert.Equal(BranchPosAuthorizationDefaults.CompleteIdentityPolicyName, endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy);
            Assert.Equal(0, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
            var principal = authenticated ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(BranchPosClaimTypes.BranchId, branch),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-1"), new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-1")], "SyntheticAuthentication")) : new ClaimsPrincipal();
            var context = new DefaultHttpContext { RequestServices = _app.Services, User = principal, RequestAborted = cancellationToken };
            context.Request.Method = "GET"; context.Request.Path = BranchOrderingEndpoints.Prefix + "/cloud-replay-status";
            context.Request.Scheme = https ? "https" : "http"; context.Request.QueryString = new QueryString(query);
            context.Request.ContentLength = unknownLength ? null : body ? 1 : 0; context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(body));
            await using var output = new MemoryStream(); context.Response.Body = output;
            var application = new ApplicationBuilder(_app.Services); application.UseBranchOrderingGuards(_configuration); application.Run(endpoint.RequestDelegate!);
            await application.Build()(context);
            var bytes = output.ToArray(); using var document = bytes.Length == 0 ? null : JsonDocument.Parse(bytes);
            return (context.Response.StatusCode, document?.RootElement.Clone() ?? default, context.Response.Headers.CacheControl.ToString());
        }
        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
    private sealed record BodyDetection(bool CanHaveBody) : IHttpRequestBodyDetectionFeature;
}
