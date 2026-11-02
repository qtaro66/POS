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

public sealed class BranchCloudOutboxStatusHttpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Read_only_query_cannot_create_a_missing_database_and_validates_bounds_before_io()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-outbox-" + Guid.NewGuid().ToString("N") + ".db");
        var query = new SqliteBranchCloudOutboxStatusQuery(path);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.ListAsync("BRANCH-01", 201));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.ListAsync("BRANCH-01", 50, 0));
        await Assert.ThrowsAsync<SqliteException>(() => query.ListAsync("BRANCH-01", 50));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Query_maps_identity_and_preserves_int64_sequences_as_strings()
    {
        await using var fixture = new Fixture();
        var result = await fixture.Call("?afterSequence=9007199254740993", "BRANCH-02");
        Assert.Equal(200, result.Status);
        Assert.Contains("no-store", result.Cache);
        Assert.Equal("BRANCH-02", fixture.Query.Branch);
        Assert.Equal(50, fixture.Query.Limit);
        Assert.Equal(9007199254740993L, fixture.Query.After);
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), result.Json.GetProperty("items")[0].GetProperty("branchSequence").GetString());
        Assert.Equal("9007199254740993", result.Json.GetProperty("items")[0].GetProperty("expectedBranchSequence").GetString());
        Assert.Equal(Now, result.Json.GetProperty("observedAt").GetDateTimeOffset());
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
    public async Task Invalid_or_scope_override_queries_cannot_reach_storage(string query)
    {
        await using var fixture = new Fixture();
        Assert.Equal(400, (await fixture.Call(query)).Status);
        Assert.Equal(0, fixture.Query.Calls);
    }

    [Fact]
    public async Task Identity_https_and_body_guards_prevent_reads_and_disabled_ordering_maps_nothing()
    {
        await using var fixture = new Fixture();
        Assert.Equal(401, (await fixture.Call(authenticated: false)).Status);
        Assert.Equal(400, (await fixture.Call(https: false)).Status);
        Assert.Equal(413, (await fixture.Call(body: true)).Status);
        Assert.Equal(413, (await fixture.Call(body: true, unknownLength: true)).Status);
        Assert.Equal(0, fixture.Query.Calls);
        await using var disabled = new Fixture(enabled: false);
        Assert.Empty(disabled.Endpoints);
    }

    [Fact]
    public async Task Storage_errors_are_generic_and_request_cancellation_propagates()
    {
        await using var fixture = new Fixture();
        foreach (var error in new Exception[] { new SqliteException("PRIVATE path", 1), new FormatException("PRIVATE timestamp"), new OperationCanceledException("PRIVATE timeout") })
        {
            fixture.Query.Error = error;
            var result = await fixture.Call();
            Assert.Equal(503, result.Status);
            Assert.Equal("CloudOutboxStatusUnavailable", result.Json.GetProperty("code").GetString());
            Assert.DoesNotContain("PRIVATE", result.Json.GetRawText());
        }
        fixture.Query.Error = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Call(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Real_sqlite_pages_count_unacknowledged_rows_filter_branches_and_reveal_no_internal_data()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-outbox-status-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "branch.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                for (var index = 1; index <= 54; index++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = """
                        INSERT INTO cloud_sync_outbox (outbox_id, branch_id, branch_sequence, request_id, table_session_id,
                            action, table_version, created_at, processing_status, retry_count, last_error, successfully_sent_at)
                        VALUES ($id, $branch, $sequence, $id, 'SESSION-1', 'SubmitOrder', 1, $created, $status, $retry, 'PRIVATE error', $ack);
                        """;
                    insert.Parameters.AddWithValue("$id", "EVENT-" + index.ToString(CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$branch", index == 54 ? "BRANCH-02" : "BRANCH-01");
                    insert.Parameters.AddWithValue("$sequence", index == 54 ? long.MaxValue : index);
                    insert.Parameters.AddWithValue("$created", Now.AddMinutes(-2).ToString("O", CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$status", index == 52 ? "PRIVATE-STATE" : index % 3 == 0 ? "Failed" : index % 3 == 1 ? "Pending" : "Processing");
                    insert.Parameters.AddWithValue("$retry", index == 3 ? 2 : 0);
                    insert.Parameters.AddWithValue("$ack", index == 53 ? Now.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
                    await insert.ExecuteNonQueryAsync();
                }
                foreach (var newer in new[] { false, true })
                {
                    using var failure = connection.CreateCommand();
                    failure.CommandText = """
                        INSERT INTO cloud_sync_delivery_failures (failure_id, branch_id, outbox_id, branch_sequence,
                            lease_owner, claimed_at, lease_expires_at, failed_at, next_retry_at, error_code, retry_count_after, expected_branch_sequence)
                        VALUES ($id, 'BRANCH-01', 'EVENT-3', 3, 'PRIVATE owner', $claim, $lease, $failed, $next, 'PRIVATE diagnostic', 2, $expected);
                        """;
                    failure.Parameters.AddWithValue("$id", newer ? "A-NEW" : "Z-OLD");
                    failure.Parameters.AddWithValue("$claim", Now.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture));
                    failure.Parameters.AddWithValue("$lease", Now.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture));
                    failure.Parameters.AddWithValue("$failed", (newer ? Now : Now.AddTicks(-1)).ToString("O", CultureInfo.InvariantCulture));
                    failure.Parameters.AddWithValue("$next", Now.AddMinutes(2).ToString("O", CultureInfo.InvariantCulture));
                    failure.Parameters.AddWithValue("$expected", newer ? 1 : 2);
                    await failure.ExecuteNonQueryAsync();
                }
            }
            var before = await File.ReadAllBytesAsync(path);
            await using (var fixture = new Fixture(reader: new SqliteBranchCloudOutboxStatusQuery(path)))
            {
                var first = await fixture.Call();
                Assert.Equal(200, first.Status);
                var summary = first.Json.GetProperty("summary");
                Assert.Equal(52, summary.GetProperty("unacknowledgedCount").GetInt64());
                foreach (var name in new[] { "pendingCount", "processingCount", "failedCount" }) { Assert.Equal(17, summary.GetProperty(name).GetInt64()); }
                Assert.Equal(1, summary.GetProperty("otherCount").GetInt64());
                Assert.Equal(50, first.Json.GetProperty("items").GetArrayLength());
                var failed = first.Json.GetProperty("items")[2];
                Assert.Equal("1", failed.GetProperty("expectedBranchSequence").GetString());
                Assert.Equal(Now, failed.GetProperty("lastFailedAt").GetDateTimeOffset());
                var second = await fixture.Call("?afterSequence=" + first.Json.GetProperty("nextAfterSequence").GetString());
                Assert.Equal(2, second.Json.GetProperty("items").GetArrayLength());
                Assert.Equal("Other", second.Json.GetProperty("items")[1].GetProperty("processingStatus").GetString());
                Assert.Null(second.Json.GetProperty("nextAfterSequence").GetString());
                Assert.DoesNotContain("PRIVATE", first.Json.GetRawText() + second.Json.GetRawText());
                var other = await fixture.Call(branch: "BRANCH-02");
                Assert.Equal(1, other.Json.GetProperty("summary").GetProperty("unacknowledgedCount").GetInt64());
                Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), other.Json.GetProperty("items")[0].GetProperty("branchSequence").GetString());
                Assert.Equal(before, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) { File.Delete(file); }
            if (Directory.Exists(directory)) { Directory.Delete(directory); }
        }
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class QueryStub : IBranchCloudOutboxStatusQuery
    {
        public string? Branch { get; private set; }
        public int Limit { get; private set; }
        public long? After { get; private set; }
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public Task<BranchCloudOutboxStatusPage> ListAsync(string branchId, int maximumResults, long? afterSequence = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++; Branch = branchId; Limit = maximumResults; After = afterSequence;
            if (Error is not null) { throw Error; }
            return Task.FromResult(new BranchCloudOutboxStatusPage(new(1, 0, 0, 1, 0), [new("EVENT-1", long.MaxValue, "REQUEST-1", "SESSION-1",
                "SubmitOrder", "Failed", Now, 2, null, null, Now, 9007199254740993L)], null));
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly IConfiguration _configuration;
        public QueryStub Query { get; } = new();
        public IEnumerable<Endpoint> Endpoints => ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints);
        public Fixture(bool enabled = true, IBranchCloudOutboxStatusQuery? reader = null)
        {
            _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BranchOrdering:Enabled"] = enabled.ToString() }).Build();
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(new BranchOrderingRuntime(Path.Combine(Path.GetTempPath(), "unused-outbox-status-test.db"), new Clock(), reader ?? Query));
            _app = builder.Build(); _app.MapBranchOrderingIfEnabled(_configuration);
        }
        public async Task<(int Status, JsonElement Json, string Cache)> Call(string query = "", string branch = "BRANCH-01", bool authenticated = true, bool https = true, bool body = false, bool unknownLength = false, CancellationToken cancellationToken = default)
        {
            var endpoint = Endpoints.Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetBranchCloudOutboxStatus");
            Assert.Equal(BranchPosAuthorizationDefaults.CompleteIdentityPolicyName, endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy);
            Assert.Equal(0, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
            var principal = authenticated ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(BranchPosClaimTypes.BranchId, branch),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-1"), new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-1")], "SyntheticAuthentication")) : new ClaimsPrincipal();
            var context = new DefaultHttpContext { RequestServices = _app.Services, User = principal, RequestAborted = cancellationToken };
            context.Request.Method = "GET"; context.Request.Path = BranchOrderingEndpoints.Prefix + "/cloud-outbox-status";
            context.Request.Scheme = https ? "https" : "http"; context.Request.QueryString = new QueryString(query);
            context.Request.ContentLength = unknownLength ? null : body ? 1 : 0;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(body));
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
