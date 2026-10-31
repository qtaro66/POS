using System.Security.Claims;
using System.Text;
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
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchOrderingHttpApiTests
{
    [Fact]
    public async Task Open_menu_submit_and_query_use_durable_results_and_server_prices()
    {
        await using var fixture = await Fixture.CreateAsync();
        var opened = await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        Assert.Equal(200, opened.Status);
        Assert.Equal("BranchAccepted", opened.Body.GetProperty("result").GetProperty("status").GetString());
        var second = await fixture.Call("OpenBranchTable", Open("OPEN-2", "SESSION-2", "A01"));
        Assert.Equal("SESSION-1", second.Body.GetProperty("result").GetProperty("tableSessionId").GetString());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM table_sessions;"));

        await fixture.Execute("UPDATE master_data_packages SET lifecycle_status = 'Previous' WHERE master_data_version = 'OLD'; UPDATE master_data_packages SET lifecycle_status = 'Active' WHERE master_data_version = 'NEW';");
        var menu = await fixture.Call("GetBranchSessionMenu", routeValue: "SESSION-1");
        Assert.Equal("OLD", menu.Body.GetProperty("masterDataVersion").GetString());
        var item = menu.Body.GetProperty("items")[0];
        Assert.Equal(100m, item.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(10m, item.GetProperty("optionGroups")[0].GetProperty("options")[0].GetProperty("unitPrice").GetDecimal());

        var request = Submit("ORDER-1");
        var submitted = await fixture.Call("SubmitBranchOrder", request);
        Assert.Equal(200, submitted.Status);
        var repeated = await fixture.Call("SubmitBranchOrder", request);
        Assert.Equal(submitted.Body.GetRawText(), repeated.Body.GetRawText());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM order_batches;"));
        Assert.Equal("BRANCH-01:ORDER-1:batch:1", await fixture.Scalar("SELECT order_batch_id FROM order_batches;"));
        Assert.Equal("2", await fixture.Scalar("SELECT COUNT(*) FROM order_lines;"));
        Assert.Equal("100", await fixture.Scalar("SELECT unit_price FROM order_lines LIMIT 1;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM print_jobs;"));
        Assert.Equal("2", await fixture.Scalar("SELECT COUNT(DISTINCT fulfillment_type) FROM order_lines;"));
        var status = await fixture.Call("GetOrderingCommandStatus", routeValue: "ORDER-1");
        Assert.Equal(submitted.Body.GetRawText(), status.Body.GetRawText());
        var active = await fixture.Call("ListActiveBranchTableSessions");
        Assert.False(active.Body.GetProperty("includesAvailableTables").GetBoolean());
        Assert.Single(active.Body.GetProperty("sessions").EnumerateArray());
        Assert.Equal("no-store", status.CacheControl);
    }

    [Fact]
    public async Task Catalog_rejection_is_atomic_and_repeated_rejection_preserves_line_details()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        await fixture.Execute("INSERT INTO branch_sold_out_overrides VALUES ('BRANCH-01','ITEM-1',1,'EMP-1','DEV-1','2026-10-31T05:00:00Z');");
        var menu = await fixture.Call("GetBranchSessionMenu", routeValue: "SESSION-1");
        Assert.True(menu.Body.GetProperty("items")[0].GetProperty("availability").GetProperty("soldOut").GetBoolean());
        var rejected = await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"));
        Assert.Equal(409, rejected.Status);
        Assert.Equal("CatalogValidationFailed", rejected.Body.GetProperty("result").GetProperty("errorCode").GetString());
        Assert.Equal(2, rejected.Body.GetProperty("validationDetails").GetProperty("errors").GetArrayLength());
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM order_batches;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM print_jobs;"));
        Assert.Equal(rejected.Body.GetRawText(), (await fixture.Call("GetOrderingCommandStatus", routeValue: "ORDER-1")).Body.GetRawText());
    }

    [Theory]
    [InlineData("branchId", "BRANCH-02")]
    [InlineData("deviceId", "DEV-OTHER")]
    [InlineData("employeeId", "EMP-OTHER")]
    [InlineData("requestId", " ")]
    [InlineData("clientCreatedAt", "0001-01-01T00:00:00Z")]
    [InlineData("clientCreatedAt", "2026-10-31T12:00:00+07:00")]
    public async Task Invalid_or_forged_context_cannot_write(string property, string value)
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = JsonSerializer.SerializeToNode(Open("OPEN-1", "SESSION-1", "A01"))!;
        request["context"]![property] = value;
        Assert.Equal(400, (await fixture.Call("OpenBranchTable", request)).Status);
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM command_receipts;"));
    }

    [Fact]
    public async Task Binding_rejects_client_prices_and_invalid_fulfillment_without_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        var request = JsonSerializer.SerializeToNode(Submit("ORDER-1"))!;
        request["lines"]![0]!["unitPrice"] = 1;
        Assert.Equal(400, (await fixture.Call("SubmitBranchOrder", request)).Status);
        request["lines"]![0]!.AsObject().Remove("unitPrice");
        request["lines"]![0]!["fulfillmentType"] = "1";
        Assert.Equal(400, (await fixture.Call("SubmitBranchOrder", request)).Status);
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM order_batches;"));
    }

    [Fact]
    public async Task Missing_identity_cross_branch_reads_https_and_body_guards_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(401, (await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"), principal: new ClaimsPrincipal())).Status);
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        Assert.Equal(404, (await fixture.Call("GetOrderingCommandStatus", routeValue: "OPEN-1", principal: Principal("BRANCH-02"))).Status);
        Assert.Equal(404, (await fixture.Call("GetBranchSessionMenu", routeValue: "SESSION-1", principal: Principal("BRANCH-02"))).Status);
        var otherTables = await fixture.Call("ListActiveBranchTableSessions", principal: Principal("BRANCH-02"));
        Assert.Empty(otherTables.Body.GetProperty("sessions").EnumerateArray());
        Assert.Equal(400, (await fixture.Call("OpenBranchTable", Open("OPEN-2", "SESSION-2", "A02"), https: false)).Status);
        Assert.Equal(413, (await fixture.Call("OpenBranchTable", new { padding = new string('x', 65536) })).Status);
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM table_sessions;"));
    }

    [Fact]
    public async Task Startup_is_disabled_by_default_and_rejects_missing_authentication()
    {
        await using var fixture = await Fixture.CreateAsync();
        var configuration = new ConfigurationBuilder().Build();
        var validator = new BranchOrderingOptionsValidator(configuration);
        Assert.True(validator.Validate(null, new BranchOrderingOptions()).Succeeded);
        Assert.True(validator.Validate(null, new BranchOrderingOptions { Enabled = true, DatabasePath = fixture.Path }).Failed);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBranchOrdering(configuration);
        await using var app = builder.Build();
        app.MapBranchOrderingIfEnabled(configuration);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
        Assert.Null(app.Services.GetService<BranchOrderingRuntime>());
    }

    [Fact]
    public async Task Enabled_startup_requires_current_schema_and_same_identity_database()
    {
        await using var fixture = await Fixture.CreateAsync();
        var values = new Dictionary<string, string?>
        {
            ["BranchOrdering:Enabled"] = "true",
            ["BranchOrdering:DatabasePath"] = fixture.Path,
            ["BranchDeviceAuthentication:Enabled"] = "true",
            ["BranchDeviceAuthentication:DatabasePath"] = fixture.Path,
            ["BranchEmployeeSessions:Enabled"] = "true",
            ["BranchEmployeeSessions:DatabasePath"] = fixture.Path
        };
        var options = new BranchOrderingOptions { Enabled = true, DatabasePath = fixture.Path };
        var validator = new BranchOrderingOptionsValidator(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.True(validator.Validate(null, options).Succeeded);
        var services = new ServiceCollection();
        services.AddBranchOrdering(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.NotNull(provider.GetRequiredService<BranchOrderingRuntime>());
        values["BranchEmployeeSessions:DatabasePath"] = fixture.Path + ".other";
        Assert.True(new BranchOrderingOptionsValidator(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate(null, options).Failed);
        await fixture.Execute("PRAGMA user_version = 0;");
        Assert.True(validator.Validate(null, options).Failed);
    }

    [Fact]
    public async Task Unknown_length_body_is_limited_and_payment_pending_cannot_accept_orders()
    {
        await using var fixture = await Fixture.CreateAsync();
        var oversized = JsonSerializer.SerializeToNode(Open("OPEN-0", "SESSION-0", "A00"))!;
        oversized["tableId"] = new string('x', 65536);
        Assert.Equal(413, (await fixture.Call("OpenBranchTable", oversized, unknownLength: true)).Status);
        await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"));
        await fixture.Execute("UPDATE table_sessions SET status = 'PaymentPending' WHERE table_session_id = 'SESSION-1';");
        var rejected = await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"));
        Assert.Equal(409, rejected.Status);
        Assert.Equal("InvalidTableSessionState", rejected.Body.GetProperty("result").GetProperty("errorCode").GetString());
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM order_batches;"));
    }

    [Fact]
    public async Task Two_open_requests_share_one_session_and_one_invalid_line_rejects_whole_batch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var results = await Task.WhenAll(
            fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01")),
            fixture.Call("OpenBranchTable", Open("OPEN-2", "SESSION-2", "A01")));
        Assert.All(results, result => Assert.Equal(200, result.Status));
        var session = results[0].Body.GetProperty("result").GetProperty("tableSessionId").GetString();
        Assert.Equal(session, results[1].Body.GetProperty("result").GetProperty("tableSessionId").GetString());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM table_sessions;"));
        var order = JsonSerializer.SerializeToNode(Submit("ORDER-1"))!;
        order["context"]!["tableSessionId"] = session;
        order["lines"]![1]!["sellableItemId"] = "MISSING";
        var rejected = await fixture.Call("SubmitBranchOrder", order);
        Assert.Equal(409, rejected.Status);
        Assert.Equal(1, rejected.Body.GetProperty("validationDetails").GetProperty("errors").GetArrayLength());
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM order_lines;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM production_order_snapshots;"));
    }

    private static object Open(string requestId, string sessionId, string tableId) => new
    {
        schemaVersion = 1,
        context = Context(requestId, sessionId),
        tableId
    };

    private static object Context(string requestId, string sessionId) => new
    {
        requestId,
        branchId = "BRANCH-01",
        deviceId = "DEV-1",
        employeeId = "EMP-1",
        tableSessionId = sessionId,
        clientCreatedAt = "2026-10-31T05:00:00Z"
    };

    private static object Submit(string requestId) => new
    {
        schemaVersion = 1,
        context = Context(requestId, "SESSION-1"),
        lines = new[] { "DineIn", "Takeaway" }.Select(type => new
        {
            sellableItemId = "ITEM-1",
            quantity = 1,
            fulfillmentType = type,
            comment = "test",
            options = new[] { new { optionGroupId = "GROUP-1", sellableItemId = "OPT-1", quantity = 1 } }
        }).ToArray()
    };

    private static ClaimsPrincipal Principal(string branch = "BRANCH-01") => new(new ClaimsIdentity(
        [new Claim(BranchPosClaimTypes.BranchId, branch), new Claim(BranchPosClaimTypes.DeviceId, "DEV-1"),
            new Claim(BranchPosClaimTypes.EmployeeId, "EMP-1")], "SyntheticAuthentication"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos-ordering-api", Guid.NewGuid().ToString("N"));
        private WebApplication _app = null!;
        public string Path => System.IO.Path.Combine(_directory, "branch.db");
        private IConfiguration _configuration = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try { await fixture.InitializeAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task InitializeAsync()
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
            await Execute($$"""
                INSERT INTO master_data_packages VALUES
                  ('BRANCH-01','OLD',1,'{{new string('A', 64)}}','2026-10-31T03:00:00.0000000+00:00','2026-10-31T04:00:00.0000000+00:00','Active'),
                  ('BRANCH-01','NEW',1,'{{new string('B', 64)}}','2026-10-31T03:00:00.0000000+00:00','2026-10-31T04:00:00.0000000+00:00','Staged');
                INSERT INTO master_data_billing_policy VALUES
                  ('BRANCH-01','OLD','CALC-1','7','0',2,'AwayFromZero'),
                  ('BRANCH-01','NEW','CALC-1','7','0',2,'AwayFromZero');
                INSERT INTO master_data_sellable_items
                  (branch_id,master_data_version,sellable_item_id,item_kind,display_name,resolved_price,central_active,branch_catalog_enabled,production_station_id) VALUES
                  ('BRANCH-01','OLD','ITEM-1','Item','Test item','100',1,1,'KITCHEN-1'),
                  ('BRANCH-01','OLD','OPT-1','SubItem','Test option','10',1,1,NULL),
                  ('BRANCH-01','NEW','ITEM-1','Item','Changed item','150',1,1,'KITCHEN-1'),
                  ('BRANCH-01','NEW','OPT-1','SubItem','Changed option','20',1,1,NULL);
                INSERT INTO master_data_option_groups VALUES ('BRANCH-01','OLD','ITEM-1','GROUP-1','Choose','SubItem',1,1,0,1,0);
                INSERT INTO master_data_option_group_items VALUES ('BRANCH-01','OLD','ITEM-1','GROUP-1','OPT-1',0,0);
                """);
            _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BranchOrdering:Enabled"] = "true" }).Build();
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(new BranchOrderingRuntime(Path, TimeProvider.System));
            _app = builder.Build();
            _app.MapBranchOrderingIfEnabled(_configuration);
        }

        public async Task<(int Status, JsonElement Body, string CacheControl)> Call(string name, object? body = null,
            string? routeValue = null, ClaimsPrincipal? principal = null, bool https = true, bool unknownLength = false)
        {
            var endpoint = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints)
                .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
            Assert.Equal(BranchPosAuthorizationDefaults.CompleteIdentityPolicyName, endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy);
            var context = new DefaultHttpContext { RequestServices = _app.Services, User = principal ?? Principal() };
            context.Request.Scheme = https ? "https" : "http";
            context.Request.Path = BranchOrderingEndpoints.Prefix;
            context.Request.Method = body is null ? "GET" : "POST";
            context.Request.RouteValues["requestId"] = routeValue;
            context.Request.RouteValues["tableSessionId"] = routeValue;
            using var requestBody = new MemoryStream(Encoding.UTF8.GetBytes(body is null ? "" : JsonSerializer.Serialize(body)));
            context.Request.Body = requestBody;
            context.Request.ContentLength = unknownLength ? null : requestBody.Length;
            context.Request.ContentType = "application/json";
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(body is not null));
            await using var output = new MemoryStream();
            context.Response.Body = output;
            var application = new ApplicationBuilder(_app.Services);
            application.UseBranchOrderingGuards(_configuration);
            application.Run(endpoint.RequestDelegate!);
            await application.Build()(context);
            var bytes = output.ToArray();
            using var document = bytes.Length == 0 ? null : JsonDocument.Parse(bytes);
            return (context.Response.StatusCode, document is null ? default : document.RootElement.Clone(), context.Response.Headers.CacheControl.ToString());
        }

        public async Task Execute(string sql)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false, ForeignKeys = true }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string?> Scalar(string sql)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is not null) { await _app.DisposeAsync(); }
            var resolved = System.IO.Path.GetFullPath(_directory);
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos-ordering-api")) + System.IO.Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Unsafe fixture path."); }
            if (Directory.Exists(resolved)) { Directory.Delete(resolved, true); }
        }
    }

    private sealed class BodyDetection(bool hasBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => hasBody;
    }
}
