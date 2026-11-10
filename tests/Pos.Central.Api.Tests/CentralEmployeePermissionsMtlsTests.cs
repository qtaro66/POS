using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Branch.Api.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Central.Api.Tests;

public sealed class CentralEmployeePermissionsMtlsTests
{
    [Fact]
    public async Task Branch_certificate_pulls_only_its_snapshot_and_is_denied_after_disabling_registration()
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

            app.Configuration[certificate + "Enabled"] = "false";
            using var denied = await client.GetAsync(url, timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        finally
        {
            File.Delete(certificatePath);
            File.Delete(keyPath);
        }
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
