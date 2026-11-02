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
using Pos.Application.Printing;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Printing;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPrintAttentionHttpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Endpoint_uses_system_utc_when_no_custom_clock_is_registered()
    {
        await using var fixture = new Fixture(useClock: false);
        var before = DateTimeOffset.UtcNow;
        Assert.Equal(200, (await fixture.Call()).Status);
        Assert.InRange(fixture.Query.LastObservation, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, fixture.Query.LastObservation.Offset);
    }

    [Fact]
    public async Task Http_pages_use_real_read_only_sqlite_filtering_without_changing_the_database()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-print-http-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "branch.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                for (var index = 0; index < 53; index++)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        INSERT INTO bill_preview_snapshots (snapshot_id, branch_id, request_id, table_session_id, table_id,
                            document_label, content_schema_version, content_json, content_sha256, created_at)
                        VALUES ('SNAP-' || $id, $branch, 'REQUEST-' || $id, 'SESSION-1', 'A01', 'Bill Preview', 1, '{}', $hash, $created);
                        INSERT INTO print_jobs (print_job_id, branch_id, request_id, table_session_id, snapshot_id, purpose,
                            target_station, status, attempt_count, created_at)
                        VALUES ('PRINT-' || $id, $branch, 'REQUEST-' || $id, 'SESSION-1', 'SNAP-' || $id, 'BillPreview', 'Cashier', $status, 0, $created);
                        """;
                    command.Parameters.AddWithValue("$id", index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$branch", index == 51 ? "BRANCH-02" : "BRANCH-01");
                    command.Parameters.AddWithValue("$status", index == 52 ? "Completed" : "Unknown");
                    command.Parameters.AddWithValue("$hash", new string('A', 64));
                    command.Parameters.AddWithValue("$created", Now.AddMinutes(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                    await command.ExecuteNonQueryAsync();
                }
            }
            var before = await File.ReadAllBytesAsync(path);
            await using (var fixture = new Fixture(reader: new SqlitePrintJobAttentionQuery(path)))
            {
                var first = await fixture.Call();
                Assert.Equal(200, first.Status);
                Assert.Equal(51, first.Json.GetProperty("totalCount").GetInt64());
                Assert.Equal(50, first.Json.GetProperty("items").GetArrayLength());
                var next = await fixture.Call("?cursor=" + first.Json.GetProperty("nextCursor").GetString());
                Assert.Equal(1, next.Json.GetProperty("items").GetArrayLength());
                var ids = first.Json.GetProperty("items").EnumerateArray().Concat(next.Json.GetProperty("items").EnumerateArray())
                    .Select(item => item.GetProperty("printJobId").GetString()).ToArray();
                Assert.Equal(51, ids.Distinct().Count());
                Assert.DoesNotContain("PRINT-51", ids);
                Assert.DoesNotContain("PRINT-52", ids);
                Assert.Equal(before, await File.ReadAllBytesAsync(path));
            }
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) { File.Delete(file); }
            if (Directory.Exists(directory)) { Directory.Delete(directory); }
        }
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public async Task Endpoint_requires_both_existing_feature_flags(bool ordering, bool status, int count)
    {
        await using var fixture = new Fixture(ordering, status);
        Assert.Equal(count, fixture.Endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetBranchPrintAttention"));
    }

    [Fact]
    public async Task Read_is_branch_scoped_bounded_no_store_and_omits_internal_diagnostics()
    {
        await using var fixture = new Fixture();
        var result = await fixture.Call(branch: "BRANCH-02");
        Assert.Equal(200, result.Status);
        Assert.Contains("no-store", result.Cache);
        Assert.Equal("BRANCH-02", fixture.Query.LastBranch);
        Assert.Equal(Now, fixture.Query.LastObservation);
        Assert.Equal(50, fixture.Query.LastLimit);
        var item = result.Json.GetProperty("items")[0];
        Assert.Equal("PRINT-1", item.GetProperty("printJobId").GetString());
        Assert.Equal("SendOutcomeUnknown", item.GetProperty("attentionReason").GetString());
        Assert.False(item.TryGetProperty("lastError", out _));
        Assert.False(item.TryGetProperty("latestErrorCode", out _));
        Assert.DoesNotContain("PRIVATE", result.Json.GetRawText());
        Assert.Equal(1, fixture.Query.Calls);
    }

    [Theory]
    [InlineData(PersistentPrintJobStatus.PrintQueued, false, "AwaitingDispatch")]
    [InlineData(PersistentPrintJobStatus.PrintQueued, true, "DispatchBlocked")]
    [InlineData(PersistentPrintJobStatus.Failed, false, "SendFailed")]
    [InlineData(PersistentPrintJobStatus.Unknown, false, "SendOutcomeUnknown")]
    [InlineData(PersistentPrintJobStatus.Dispatching, false, "DispatchLeaseExpired")]
    public async Task Attention_reason_is_public_and_never_copies_an_internal_error(PersistentPrintJobStatus status, bool blocked, string reason)
    {
        await using var fixture = new Fixture();
        fixture.Query.Item = fixture.Query.Item with { Status = status, BlockedReason = blocked ? "PRIVATE configuration" : null };
        var result = await fixture.Call();
        Assert.Equal(reason, result.Json.GetProperty("items")[0].GetProperty("attentionReason").GetString());
        Assert.DoesNotContain("PRIVATE", result.Json.GetRawText());
    }

    [Fact]
    public async Task Next_cursor_preserves_keyset_and_observation_without_accepting_branch_overrides()
    {
        await using var fixture = new Fixture();
        var first = await fixture.Call();
        var cursor = first.Json.GetProperty("nextCursor").GetString()!;
        fixture.Clock.Now = Now.AddMinutes(1);
        var second = await fixture.Call("?cursor=" + cursor, branch: "BRANCH-02");
        Assert.Equal(200, second.Status);
        Assert.Equal(Now, fixture.Query.LastObservation);
        Assert.Equal("PRINT-1", fixture.Query.LastCursor?.PrintJobId);
        Assert.Equal(Now.AddMinutes(-1), fixture.Query.LastCursor?.CreatedAt);
        Assert.Equal("BRANCH-02", fixture.Query.LastBranch);
        Assert.Null(second.Json.GetProperty("nextCursor").GetString());
        Assert.Equal(400, (await fixture.Call("?branchId=BRANCH-OTHER")).Status);
        Assert.Equal(2, fixture.Query.Calls);
    }

    [Theory]
    [InlineData("?cursor=")]
    [InlineData("?cursor=bad%2Bcursor")]
    [InlineData("?cursor=a&cursor=b")]
    [InlineData("?cursor=e30")]
    [InlineData("?limit=100000")]
    public async Task Invalid_query_cannot_reach_the_reader(string query)
    {
        await using var fixture = new Fixture();
        Assert.Equal(400, (await fixture.Call(query)).Status);
        Assert.Equal(0, fixture.Query.Calls);
    }

    [Fact]
    public async Task Invalid_cursor_fields_future_timestamps_and_oversized_tokens_are_rejected()
    {
        await using var fixture = new Fixture();
        foreach (var cursor in new object[] {
            new { Version = 2, ObservedAt = Now, CreatedAt = Now, PrintJobId = "PRINT-1" },
            new { Version = 1, ObservedAt = Now.AddMinutes(1), CreatedAt = Now, PrintJobId = "PRINT-1" },
            new { Version = 1, ObservedAt = Now.ToOffset(TimeSpan.FromHours(7)), CreatedAt = Now, PrintJobId = "PRINT-1" },
            new { Version = 1, ObservedAt = Now, CreatedAt = Now, PrintJobId = " " },
            new { Version = 1, ObservedAt = Now, CreatedAt = Now, PrintJobId = "PRINT-1", Extra = true }
        })
        {
            var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(400, (await fixture.Call("?cursor=" + encoded)).Status);
        }
        Assert.Equal(400, (await fixture.Call("?cursor=" + new string('a', 1025))).Status);
        Assert.Equal(0, fixture.Query.Calls);
    }

    [Fact]
    public async Task Identity_https_and_body_guards_prevent_reads()
    {
        await using var fixture = new Fixture();
        Assert.Equal(401, (await fixture.Call(authenticated: false)).Status);
        Assert.Equal(400, (await fixture.Call(https: false)).Status);
        Assert.Equal(413, (await fixture.Call(body: true)).Status);
        Assert.Equal(413, (await fixture.Call(body: true, unknownLength: true)).Status);
        Assert.Equal(0, fixture.Query.Calls);
    }

    [Fact]
    public async Task Database_failures_are_generic_and_cancellation_is_forwarded()
    {
        await using var fixture = new Fixture();
        fixture.Query.Error = new SqliteException("PRIVATE database path", 1);
        var result = await fixture.Call();
        Assert.Equal(503, result.Status);
        Assert.Equal("PrintAttentionUnavailable", result.Json.GetProperty("code").GetString());
        Assert.DoesNotContain("PRIVATE", result.Json.GetRawText());
        fixture.Query.Error = new FormatException("PRIVATE corrupt timestamp");
        Assert.Equal(503, (await fixture.Call()).Status);
        fixture.Query.Error = new OperationCanceledException("PRIVATE reader timeout");
        Assert.Equal(503, (await fixture.Call()).Status);
        fixture.Query.Error = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Call(cancellationToken: cancellation.Token));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BranchPrintAttentionHttpTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class QueryStub : IPrintJobAttentionQuery
    {
        public PrintJobAttentionItem Item { get; set; } = new("PRINT-1", "REQUEST-1", TableSessionId.From("SESSION-1"), "SNAPSHOT-1",
            PrintJobPurpose.KitchenOrder, new PrintTargetStation("KITCHEN-1"), PersistentPrintJobStatus.Unknown, Now.AddMinutes(-1),
            "PRINTER-1", null, null, "PRIVATE stack trace", PersistentPrintAttemptStatus.Unknown, "PRIVATE error", Now);
        public int Calls { get; private set; }
        public string? LastBranch { get; private set; }
        public DateTimeOffset LastObservation { get; private set; }
        public int LastLimit { get; private set; }
        public PrintJobAttentionCursor? LastCursor { get; private set; }
        public Exception? Error { get; set; }
        public Task<PrintJobAttentionPage> ListAsync(string branchId, DateTimeOffset observedAt, int maximumResults, PrintJobAttentionCursor? cursor = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++; LastBranch = branchId; LastObservation = observedAt; LastLimit = maximumResults; LastCursor = cursor;
            if (Error is not null) { throw Error; }
            return Task.FromResult(new PrintJobAttentionPage(2, [Item], cursor is null ? new PrintJobAttentionCursor(Item.CreatedAt, Item.PrintJobId) : null));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly IConfiguration _configuration;
        public QueryStub Query { get; } = new();
        public Clock Clock { get; } = new();
        public IEnumerable<Endpoint> Endpoints => ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints);
        public Fixture(bool ordering = true, bool status = true, IPrintJobAttentionQuery? reader = null, bool useClock = true)
        {
            _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchOrdering:Enabled"] = ordering.ToString(),
                ["PrintStatusQueries:Enabled"] = status.ToString()
            }).Build();
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<IPrintJobAttentionQuery>(reader ?? Query);
            if (useClock) { builder.Services.AddSingleton<TimeProvider>(Clock); }
            builder.Services.AddSingleton(new BranchOrderingRuntime(Path.Combine(Path.GetTempPath(), "unused-print-attention-test.db"), TimeProvider.System));
            _app = builder.Build();
            _app.MapBranchOrderingIfEnabled(_configuration);
        }
        public async Task<(int Status, JsonElement Json, string Cache)> Call(string query = "", string branch = "BRANCH-01", bool authenticated = true, bool https = true, bool body = false, CancellationToken cancellationToken = default, bool unknownLength = false)
        {
            var endpoint = Endpoints.Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetBranchPrintAttention");
            Assert.Equal(BranchPosAuthorizationDefaults.CompleteIdentityPolicyName, endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy);
            Assert.Equal(0, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
            var principal = authenticated ? new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(BranchPosClaimTypes.BranchId, branch), new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-1"),
                new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-1")], "SyntheticAuthentication")) : new ClaimsPrincipal();
            var context = new DefaultHttpContext { RequestServices = _app.Services, User = principal, RequestAborted = cancellationToken };
            context.Request.Scheme = https ? "https" : "http";
            context.Request.Path = BranchOrderingEndpoints.Prefix + "/print-attention";
            context.Request.Method = "GET";
            context.Request.QueryString = new QueryString(query);
            context.Request.ContentLength = unknownLength ? null : body ? 1 : 0;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(body));
            await using var output = new MemoryStream();
            context.Response.Body = output;
            var application = new ApplicationBuilder(_app.Services);
            application.UseBranchOrderingGuards(_configuration);
            application.Run(endpoint.RequestDelegate!);
            await application.Build()(context);
            var bytes = output.ToArray();
            using var document = bytes.Length == 0 ? null : JsonDocument.Parse(bytes);
            return (context.Response.StatusCode, document?.RootElement.Clone() ?? default, context.Response.Headers.CacheControl.ToString());
        }
        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    private sealed record BodyDetection(bool CanHaveBody) : IHttpRequestBodyDetectionFeature;
}
