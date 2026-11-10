using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Central.Api.Tests;

public sealed class CentralEmployeePermissionsMtlsTests
{
    [Fact]
    public async Task Branch_certificate_and_cached_permissions_control_mobile_sold_out_changes()
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Loopback test CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        var now = DateTimeOffset.UtcNow;
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(2));
        using var serverKey = RSA.Create(2048);
        using var branchKey = RSA.Create(2048);
        using var server = Issue(root, serverKey, now, client: false);
        using var branch = Issue(root, branchKey, now, client: true);

        var builder = WebApplication.CreateBuilder();
        var certificate = CentralBranchCertificateAuthentication.RegistryPath + ":0:";
        var source = CentralBranchEmployeePermissions.Source;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [certificate + "Sha256"] = branch.GetCertHashString(HashAlgorithmName.SHA256),
            [certificate + "BranchId"] = "B1",
            [certificate + "Enabled"] = "true",
            [source + ":0:BranchId"] = "B1",
            [source + ":0:Revision"] = "1",
            [source + ":0:Employees:0:EmployeeId"] = "E1",
            [source + ":0:Employees:0:Active"] = "true",
            [source + ":0:Employees:0:SetSoldOut"] = "true",
            [source + ":1:BranchId"] = "B2",
            [source + ":1:Revision"] = "2"
        });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
            listener.UseHttps(https =>
            {
                https.ServerCertificate = server;
                https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                https.ClientCertificateValidation = (leaf, _, _) => TrustTestRoot(leaf, root);
            })));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, CentralBranchCertificateAuthentication>(
            CentralBranchCertificateAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorizationBuilder().AddPolicy(CentralBranchEmployeePermissions.Policy, policy =>
        {
            policy.AddAuthenticationSchemes(CentralBranchCertificateAuthentication.SchemeName);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(CentralCloudClaimTypes.BranchId);
        });
        await using var app = builder.Build();
        app.MapGet(CentralBranchEmployeePermissions.Route, CentralBranchEmployeePermissions.Read)
            .RequireAuthorization(CentralBranchEmployeePermissions.Policy);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(timeout.Token);
        var url = new Uri(new Uri(app.Urls.Single()), CentralBranchEmployeePermissions.Route + "?branchId=B2");

        var stem = Path.Combine(Path.GetTempPath(), "pos-mtls-" + Guid.NewGuid().ToString("N"));
        var certificatePath = stem + ".crt";
        var keyPath = stem + ".key";
        var cachePath = stem + ".json";
        var databasePath = stem + ".db";
        try
        {
            await File.WriteAllTextAsync(certificatePath, branch.ExportCertificatePem(), timeout.Token);
            await File.WriteAllTextAsync(keyPath, branchKey.ExportPkcs8PrivateKeyPem(), timeout.Token);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchCentralMtls:CertificatePath"] = certificatePath,
                ["BranchCentralMtls:PrivateKeyPath"] = keyPath
            }).Build();
            var factory = new BranchCertificateHttpHandlerFactory(configuration);
            var handler = Assert.IsAssignableFrom<HttpClientHandler>(factory.CreateHandler());
            handler.UseProxy = false;
            handler.ServerCertificateCustomValidationCallback = (_, leaf, _, errors) =>
                leaf is not null && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
                && TrustTestRoot(leaf, root);
            using var client = new HttpClient(handler);
            using var response = await client.GetAsync(url, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var snapshot = await response.Content.ReadFromJsonAsync<BranchEmployeePermissionSnapshot>(timeout.Token);
            Assert.NotNull(snapshot);
            Assert.Equal("B1", snapshot.BranchId);
            Assert.True(Assert.Single(snapshot.Employees).SetSoldOut);

            var clients = new LoopbackClientFactory(handler, url);
            var cache = new BranchEmployeePermissionCache("B1", cachePath);
            using var polling = new BranchEmployeePermissionPollingService(
                clients, cache, NullLogger<BranchEmployeePermissionPollingService>.Instance);
            Assert.Equal(PermissionRefreshOutcome.Updated, await polling.RefreshAsync(timeout.Token));
            await InitializeBranchAsync(databasePath, timeout.Token);
            var runtime = new BranchOrderingRuntime(databasePath, TimeProvider.System);
            var mobile = CreateMobilePrincipal();
            var set = new BranchSoldOutAdjustmentRequest(Guid.NewGuid(), "I1", false, true);
            await AssertAdjustmentAsync(200, set, cache);
            Assert.Single((await runtime.SoldOut.ReadAsync("B1", "", 0, timeout.Token)).Items);

            app.Configuration[certificate + "Enabled"] = "false";
            using var denied = await client.GetAsync(url, timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            var restoredCache = new BranchEmployeePermissionCache("B1", cachePath);
            using var restartedPolling = new BranchEmployeePermissionPollingService(
                clients, restoredCache, NullLogger<BranchEmployeePermissionPollingService>.Instance);
            Assert.Equal(PermissionRefreshOutcome.Unavailable, await restartedPolling.RefreshAsync(timeout.Token));
            await AssertAdjustmentAsync(200, new(Guid.NewGuid(), "I1", true, false), restoredCache);
            Assert.Empty((await runtime.SoldOut.ReadAsync("B1", "", 0, timeout.Token)).Items);

            app.Configuration[certificate + "Enabled"] = "true";
            app.Configuration[source + ":0:Revision"] = "2";
            app.Configuration[source + ":0:Employees:0:SetSoldOut"] = "false";
            Assert.Equal(PermissionRefreshOutcome.Updated, await restartedPolling.RefreshAsync(timeout.Token));
            await AssertAdjustmentAsync(403, set with { OperationId = Guid.NewGuid() }, restoredCache);
            Assert.Empty((await runtime.SoldOut.ReadAsync("B1", "", 0, timeout.Token)).Items);
            await AssertAuditAsync(databasePath, timeout.Token);

            async Task AssertAdjustmentAsync(int status, BranchSoldOutAdjustmentRequest request, BranchEmployeePermissionCache permissions)
            {
                var result = await BranchSoldOutAdjustmentHttpHandler.HandleAsync(mobile, request, runtime, timeout.Token, permissions);
                Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            }
        }
        finally
        {
            File.Delete(certificatePath);
            File.Delete(keyPath);
            File.Delete(cachePath);
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
    }

    private static ClaimsPrincipal CreateMobilePrincipal() => new([
        new ClaimsIdentity([
            new Claim(BranchPosClaimTypes.BranchId, "B1"),
            new Claim(BranchPosClaimTypes.DeviceId, "D1"),
            new Claim(BranchPosClaimTypes.DeviceRole, "Mobile"),
            new Claim(BranchPosClaimTypes.EmployeeId, "E1")
        ], BranchEmployeeSessionAuthenticationDefaults.SchemeName)
    ]);

    private static async Task InitializeBranchAsync(string path, CancellationToken cancellationToken)
    {
        await new SqliteTableSessionCommandStore(path).InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO master_data_packages
            VALUES ('B1','V1',1,printf('%064d',0),'2026-11-01','2026-11-01','Active');
            INSERT INTO master_data_sellable_items
              (branch_id,master_data_version,sellable_item_id,item_kind,display_name,central_active,branch_catalog_enabled)
            VALUES ('B1','V1','I1','Item','Demo item',1,1);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class LoopbackClientFactory(HttpMessageHandler handler, Uri origin) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = origin };
    }

    private static async Task AssertAuditAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT branch_id, sellable_item_id, employee_id, device_id, previous_sold_out, current_sold_out
            FROM branch_sold_out_audit ORDER BY previous_sold_out;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        foreach (var previous in new[] { 0, 1 })
        {
            Assert.True(await reader.ReadAsync(cancellationToken));
            Assert.Equal("B1", reader.GetString(0));
            Assert.Equal("I1", reader.GetString(1));
            Assert.Equal("E1", reader.GetString(2));
            Assert.Equal("D1", reader.GetString(3));
            Assert.Equal(previous, reader.GetInt32(4));
            Assert.Equal(1 - previous, reader.GetInt32(5));
        }
        Assert.False(await reader.ReadAsync(cancellationToken));
    }

    private static X509Certificate2 Issue(X509Certificate2 root, RSA key, DateTimeOffset now, bool client)
    {
        var request = new CertificateRequest(client ? "CN=Test branch" : "CN=Loopback test server",
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
        if (!client)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
        }
        using var leaf = request.Create(root, now.AddHours(-1), now.AddHours(1), RandomNumberGenerator.GetBytes(16));
        using var withKey = leaf.CopyWithPrivateKey(key);
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), null);
    }

    private static bool TrustTestRoot(X509Certificate2 leaf, X509Certificate2 root)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        return chain.Build(leaf);
    }
}
