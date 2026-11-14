using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Security;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Theory]
    [InlineData("PcPos")]
    [InlineData("Mobile")]
    public async Task Provisioned_device_role_survives_https_login_and_authorizes_sold_out(string deviceRole)
    {
        await using var fixture = await Fixture.CreateAsync();
        var credential = BranchDeviceCredentialFormat.Generate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture.Path, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(timeout.Token);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO branch_devices (branch_id, device_id, display_name, lifecycle_status, paired_by_employee_id, paired_at)
                VALUES ('BRANCH-01', 'DEV-1', 'Test device', 'Active', 'EMP-1', '2026-11-10T00:00:00Z');
                INSERT INTO branch_device_credentials (credential_id, branch_id, device_id, secret_hash, lifecycle_status, issued_at)
                VALUES ($credentialId, 'BRANCH-01', 'DEV-1', $hash, 'Active', '2026-11-10T00:00:00Z');
                INSERT INTO branch_employee_status (branch_id, employee_id, lifecycle_status, source_updated_at, synchronized_at)
                VALUES ('BRANCH-01', 'EMP-1', 'Active', '2026-11-10T00:00:00Z', '2026-11-10T00:00:00Z');
                """;
            command.Parameters.AddWithValue("$credentialId", credential.CredentialId);
            command.Parameters.AddWithValue("$hash", credential.CopySecretHash());
            await command.ExecuteNonQueryAsync(timeout.Token);
        }

        using var certificate = BranchMobileHostingTests.CreateCertificate();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate)));
        const string role = "BranchDeviceAuthentication:ProvisionedRoles:0:";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BranchDeviceAuthentication:Enabled"] = "true",
            ["BranchDeviceAuthentication:DatabasePath"] = fixture.Path,
            [role + "BranchId"] = "BRANCH-01",
            [role + "DeviceId"] = "DEV-1",
            [role + "Role"] = deviceRole,
            ["BranchEmployeeSessions:Enabled"] = "true",
            ["BranchEmployeeSessions:DatabasePath"] = fixture.Path,
            ["BranchEmployeeSessions:Lifetime"] = "08:00:00",
            ["BranchOrdering:Enabled"] = "true",
            ["BranchOrdering:DatabasePath"] = fixture.Path
        });
        if (deviceRole == "Mobile")
        {
            var cache = new BranchEmployeePermissionCache("BRANCH-01", fixture.Path + ".permissions.json");
            await using var snapshot = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
                new BranchEmployeePermissionSnapshot(1, "BRANCH-01", 1, [new("EMP-1", true, true)]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.True(await cache.ApplyAsync(snapshot, timeout.Token));
            builder.Services.AddSingleton(cache);
        }
        builder.Services.AddSingleton<IBranchEmployeePinValidator, TestPinValidator>();
        builder.Services.AddBranchDeviceAuthentication(builder.Configuration);
        builder.Services.AddBranchEmployeeSessions(builder.Configuration);
        builder.Services.AddBranchOrdering(builder.Configuration);
        await using var app = builder.Build();
        app.UseBranchOrderingGuards(app.Configuration);
        app.UseBranchEmployeeSessionHttpsOnly(app.Configuration);
        app.UseRouting();
        app.UseBranchIdentity(app.Configuration);
        app.MapBranchEmployeeSessionLoginEndpointIfEnabled(app.Configuration);
        app.MapBranchOrderingIfEnabled(app.Configuration);
        await app.StartAsync(timeout.Token);

        using var client = new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, actual, _, errors) =>
                actual is not null && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
                && actual.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256)
        })
        { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", credential.Token);
        using var unauthorized = await client.GetAsync("/api/v1/ordering/table-sessions", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/v1/employee-sessions", new BranchEmployeeSessionLoginRequest(1, TestPinValidator.Pin), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var employee = await login.Content.ReadFromJsonAsync<BranchEmployeeSessionLoginResponse>(timeout.Token);
        Assert.NotNull(employee);
        client.DefaultRequestHeaders.Add(BranchEmployeeSessionAuthenticationDefaults.HeaderName, employee.SessionToken);

        using var open = await client.PostAsJsonAsync("/api/v1/ordering/tables/open", Open("OPEN-ROLE", "SESSION-ROLE", "A01"), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        using var capability = await client.GetFromJsonAsync<JsonDocument>("/api/v1/ordering/sold-out/capability", timeout.Token);
        Assert.True(capability!.RootElement.GetProperty("canSetSoldOut").GetBoolean());
        using var change = await client.PostAsJsonAsync("/api/v1/ordering/sold-out/adjustments",
            new BranchSoldOutAdjustmentRequest(Guid.NewGuid(), "ITEM-1", false, true), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        using var menu = await client.GetFromJsonAsync<JsonDocument>("/api/v1/ordering/table-sessions/SESSION-ROLE/menu", timeout.Token);
        Assert.True(menu!.RootElement.GetProperty("items")[0].GetProperty("availability").GetProperty("soldOut").GetBoolean());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM branch_sold_out_audit;"));

        app.Configuration[role + "Role"] = null;
        using var denied = await client.PostAsJsonAsync("/api/v1/ordering/sold-out/adjustments",
            new BranchSoldOutAdjustmentRequest(Guid.NewGuid(), "ITEM-1", true, false), timeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var ended = await client.PostAsync("/api/v1/ordering/session/end", null, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        using var afterLock = await client.GetAsync("/api/v1/ordering/table-sessions", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLock.StatusCode);
    }

    private sealed class TestPinValidator : IBranchEmployeePinValidator
    {
        public const string Pin = "123456";

        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(string branchId, ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VerifiedBranchEmployee?>(branchId == "BRANCH-01" && pin.Span.SequenceEqual(Pin)
                ? new VerifiedBranchEmployee(branchId, "EMP-1")
                : null);
    }
}
