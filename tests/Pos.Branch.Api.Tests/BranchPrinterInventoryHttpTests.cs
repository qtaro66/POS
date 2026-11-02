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
using Pos.Application.Printing;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPrinterInventoryHttpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public async Task Inventory_requires_both_existing_feature_flags(bool ordering, bool status, int expected)
    {
        await using var fixture = new Fixture(ordering, status);
        Assert.Equal(expected, fixture.Endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetBranchPrinterInventory"));
    }
    [Fact]
    public async Task Read_uses_authenticated_branch_and_fixed_limit_without_claiming_device_health()
    {
        await using var fixture = new Fixture();
        var result = await fixture.Call("?afterPrinterId=PRINTER-00", branch: "BRANCH-02");
        Assert.Equal(200, result.Status); Assert.Contains("no-store", result.Cache);
        Assert.Equal("BRANCH-02", fixture.Query.Branch); Assert.Equal(50, fixture.Query.Limit); Assert.Equal("PRINTER-00", fixture.Query.After);
        var item = result.Json.GetProperty("items")[0];
        Assert.Equal("printer.branch.test", item.GetProperty("host").GetString());
        Assert.False(item.TryGetProperty("deviceStatus", out _)); Assert.False(item.TryGetProperty("drawerPulseCommandHex", out _));
        Assert.Equal("SnmpPrinterMib", item.GetProperty("statusMode").GetString());
        Assert.Equal(Now, result.Json.GetProperty("observedAt").GetDateTimeOffset());
    }
    [Theory]
    [InlineData("printer.branch.test", "printer.branch.test")]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("2001:db8::10", "2001:db8::10")]
    [InlineData("[2001:db8::10]", "[2001:db8::10]")]
    [InlineData("fe80::1%3", "fe80::1%3")]
    [InlineData("tcp://user:PRIVATE@printer.branch.test", null)]
    [InlineData("user:PRIVATE@printer.branch.test", null)]
    [InlineData("printer.branch.test/path?token=PRIVATE", null)]
    [InlineData("printer.branch.test:9100", null)]
    [InlineData(" PRIVATE ", null)]
    public async Task Host_projection_never_returns_url_credentials_or_connection_strings(string host, string? expected)
    {
        await using var fixture = new Fixture(); fixture.Query.Item = fixture.Query.Item with { Host = host };
        var result = await fixture.Call(); Assert.Equal(200, result.Status);
        Assert.Equal(expected, result.Json.GetProperty("items")[0].GetProperty("host").GetString());
        Assert.DoesNotContain("PRIVATE", result.Json.GetRawText());
    }
    [Theory]
    [InlineData("?afterPrinterId=")]
    [InlineData("?afterPrinterId=%20PRINTER-1")]
    [InlineData("?afterPrinterId=PRINTER%0A1")]
    [InlineData("?afterPrinterId=A&afterPrinterId=B")]
    [InlineData("?branchId=OTHER")]
    [InlineData("?limit=100000")]
    public async Task Invalid_or_scope_override_queries_never_reach_storage(string query)
    {
        await using var fixture = new Fixture(); Assert.Equal(400, (await fixture.Call(query)).Status); Assert.Equal(0, fixture.Query.Calls);
    }
    [Fact]
    public async Task Identity_https_and_known_or_unknown_length_body_guards_prevent_reads()
    {
        await using var fixture = new Fixture();
        Assert.Equal(401, (await fixture.Call(authenticated: false)).Status); Assert.Equal(400, (await fixture.Call(https: false)).Status);
        Assert.Equal(413, (await fixture.Call(body: true)).Status); Assert.Equal(413, (await fixture.Call(body: true, unknownLength: true)).Status);
        Assert.Equal(400, (await fixture.Call("?afterPrinterId=" + new string('x', 129))).Status); Assert.Equal(0, fixture.Query.Calls);
    }
    [Fact]
    public async Task Failures_are_generic_and_cancellation_propagates()
    {
        await using var fixture = new Fixture();
        foreach (var error in new Exception[] { new SqliteException("PRIVATE path", 1), new FormatException("PRIVATE timestamp"), new OperationCanceledException("PRIVATE timeout") })
        {
            fixture.Query.Error = error; var result = await fixture.Call(); Assert.Equal(503, result.Status);
            Assert.Equal("PrinterInventoryUnavailable", result.Json.GetProperty("code").GetString()); Assert.DoesNotContain("PRIVATE", result.Json.GetRawText());
        }
        fixture.Query.Error = null; using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Call(cancellationToken: cancellation.Token));
    }
    [Fact]
    public async Task Read_only_query_cannot_create_missing_database_and_validates_bounds_before_io()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-inventory-" + Guid.NewGuid().ToString("N") + ".db");
        var query = new SqliteBranchPrinterInventoryQuery(path);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.ListAsync("BRANCH-01", 201));
        await Assert.ThrowsAsync<ArgumentException>(() => query.ListAsync("BRANCH-01", 50, " "));
        await Assert.ThrowsAsync<SqliteException>(() => query.ListAsync("BRANCH-01", 50)); Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task Real_sqlite_inventory_includes_inactive_profiles_and_pages_without_mutation_or_private_fields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-printer-inventory-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "branch.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                for (var index = 0; index < 52; index++)
                {
                    using var seed = connection.CreateCommand();
                    seed.CommandText = """
                        INSERT INTO printer_profiles (printer_id, branch_id, display_name, production_station_id, host, port, protocol,
                            compatibility_profile_id, manufacturer, model, product_code, firmware_version, paper_width_millimeters, dpi,
                            printable_width_dots, render_mode, thai_encoding_mode, status_mode, supports_cash_drawer, cutter_command_hex,
                            drawer_pulse_command_hex, status_command_profile, connect_timeout_ms, send_timeout_ms,
                            maximum_automatic_retries_before_send, automatic_retry_backoff_ms, is_active, updated_at)
                        VALUES ($id, $branch, 'Example printer', $station, $host, 9100, 'RawTcpEscPos', 'TEST-PROFILE', 'Example',
                            'Thermal Example', CASE WHEN $active = 0 THEN '' ELSE 'TEST-80' END, 'test.1', 80, 203, 576, 'BitmapRaster', 'UnicodeRaster', 'Unsupported',
                            1, '0000', '0000', 'PRIVATE status parameters', 10, 10, 0, 1, $active, $updated);
                        """;
                    seed.Parameters.AddWithValue("$id", "PRINTER-" + index.ToString("D2", CultureInfo.InvariantCulture));
                    seed.Parameters.AddWithValue("$branch", index == 51 ? "BRANCH-02" : "BRANCH-01");
                    seed.Parameters.AddWithValue("$station", "STATION-" + index.ToString(CultureInfo.InvariantCulture));
                    seed.Parameters.AddWithValue("$host", index == 1 ? "tcp://user:PRIVATE@printer.branch.test" : "printer.branch.test");
                    seed.Parameters.AddWithValue("$active", index % 2 == 0); seed.Parameters.AddWithValue("$updated", Now.ToString("O", CultureInfo.InvariantCulture));
                    await seed.ExecuteNonQueryAsync();
                }
            }
            var before = await File.ReadAllBytesAsync(path);
            await using (var fixture = new Fixture(reader: new SqliteBranchPrinterInventoryQuery(path)))
            {
                var first = await fixture.Call(); Assert.Equal(200, first.Status);
                Assert.Equal(51, first.Json.GetProperty("totalCount").GetInt64()); Assert.Equal(26, first.Json.GetProperty("activeCount").GetInt64());
                Assert.Equal(50, first.Json.GetProperty("items").GetArrayLength());
                Assert.False(first.Json.GetProperty("items")[1].GetProperty("isActive").GetBoolean());
                Assert.Null(first.Json.GetProperty("items")[1].GetProperty("host").GetString());
                Assert.Null(first.Json.GetProperty("items")[1].GetProperty("productCode").GetString());
                var second = await fixture.Call("?afterPrinterId=" + first.Json.GetProperty("nextAfterPrinterId").GetString());
                Assert.Equal(1, second.Json.GetProperty("items").GetArrayLength()); Assert.Equal("PRINTER-50", second.Json.GetProperty("items")[0].GetProperty("printerId").GetString());
                Assert.Null(second.Json.GetProperty("nextAfterPrinterId").GetString());
                Assert.DoesNotContain("PRIVATE", first.Json.GetRawText() + second.Json.GetRawText());
                var other = await fixture.Call(branch: "BRANCH-02"); Assert.Equal(1, other.Json.GetProperty("totalCount").GetInt64());
                Assert.Equal("PRINTER-51", other.Json.GetProperty("items")[0].GetProperty("printerId").GetString());
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
    private sealed class QueryStub : IBranchPrinterInventoryQuery
    {
        public BranchPrinterInventoryItem Item { get; set; } = new("PRINTER-01", "Example printer", "CASHIER", "printer.branch.test", 9100,
            "RawTcpEscPos", "TEST-PROFILE", "Example", "Thermal Example", "TEST-80", "test.1", 80, "SnmpPrinterMib", true, false, Now);
        public string? Branch { get; private set; }
        public int Limit { get; private set; }
        public string? After { get; private set; }
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public Task<BranchPrinterInventoryPage> ListAsync(string branchId, int maximumResults, string? afterPrinterId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; Branch = branchId; Limit = maximumResults; After = afterPrinterId;
            if (Error is not null) { throw Error; }
            return Task.FromResult(new BranchPrinterInventoryPage(1, 0, [Item], null));
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly IConfiguration _configuration;
        public QueryStub Query { get; } = new();
        public IEnumerable<Endpoint> Endpoints => ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints);
        public Fixture(bool ordering = true, bool status = true, IBranchPrinterInventoryQuery? reader = null)
        {
            _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchOrdering:Enabled"] = ordering.ToString(),
                ["PrintStatusQueries:Enabled"] = status.ToString()
            }).Build();
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<IBranchPrinterInventoryQuery>(reader ?? Query); builder.Services.AddSingleton<TimeProvider>(new Clock());
            var unused = Path.Combine(Path.GetTempPath(), "unused-inventory-test.db");
            builder.Services.AddSingleton<IPrintJobAttentionQuery>(new SqlitePrintJobAttentionQuery(unused));
            builder.Services.AddSingleton(new BranchOrderingRuntime(unused, TimeProvider.System));
            _app = builder.Build(); _app.MapBranchOrderingIfEnabled(_configuration);
        }
        public async Task<(int Status, JsonElement Json, string Cache)> Call(string query = "", string branch = "BRANCH-01", bool authenticated = true, bool https = true, bool body = false, bool unknownLength = false, CancellationToken cancellationToken = default)
        {
            var endpoint = Endpoints.Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetBranchPrinterInventory");
            Assert.Equal(BranchPosAuthorizationDefaults.CompleteIdentityPolicyName, endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy);
            Assert.Equal(0, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
            var principal = authenticated ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(BranchPosClaimTypes.BranchId, branch),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-1"), new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-1")], "SyntheticAuthentication")) : new ClaimsPrincipal();
            var context = new DefaultHttpContext { RequestServices = _app.Services, User = principal, RequestAborted = cancellationToken };
            context.Request.Method = "GET"; context.Request.Path = BranchOrderingEndpoints.Prefix + "/printer-inventory";
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
