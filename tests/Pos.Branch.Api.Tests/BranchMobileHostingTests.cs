using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Pos.Branch.Api.Hosting;

namespace Pos.Branch.Api.Tests;

public sealed class BranchMobileHostingTests
{
    [Fact]
    public async Task Hosting_is_opt_in_and_requires_build_output()
    {
        await using var app = WebApplication.CreateBuilder().Build();
        app.MapBranchMobileIfEnabled(new ConfigurationBuilder().Build());
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources);
        Assert.Throws<InvalidOperationException>(() => app.MapBranchMobileIfEnabled(Configuration("relative")));
    }

    [Fact]
    public async Task Serves_the_mobile_build_on_https_without_a_fallback_for_other_paths()
    {
        var root = Directory.CreateTempSubdirectory("pos-mobile-host-");
        var index = Path.Combine(root.FullName, "index.html");
        var assets = Directory.CreateDirectory(Path.Combine(root.FullName, "assets"));
        var script = Path.Combine(assets.FullName, "index-Demo.js");
        var unsupported = Path.Combine(assets.FullName, "example.txt");
        try
        {
            await File.WriteAllTextAsync(index, """<!doctype html><script type="module" src="/mobile/assets/index-Demo.js"></script>""");
            await File.WriteAllTextAsync(script, "export {};");
            await File.WriteAllTextAsync(unsupported, "not an application asset");
            using var certificate = CreateCertificate();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
                options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate));
            });
            await using var app = builder.Build();
            app.MapBranchMobileIfEnabled(Configuration(root.FullName));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await app.StartAsync(timeout.Token);
            var secure = new Uri(app.Urls.Single(url => url.StartsWith("https://", StringComparison.Ordinal)));
            var insecure = new Uri(app.Urls.Single(url => url.StartsWith("http://", StringComparison.Ordinal)));
            using var client = new HttpClient(new HttpClientHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, actual, _, errors) =>
                    actual is not null && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
                    && actual.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256)
            });
            using var redirect = await client.GetAsync(new Uri(secure, "/mobile"), timeout.Token);
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            Assert.Equal("/mobile/", redirect.Headers.Location?.OriginalString);
            using var page = await client.GetAsync(new Uri(secure, "/mobile/"), timeout.Token);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
            Assert.True(page.Headers.CacheControl?.NoStore);
            Assert.Contains("connect-src 'self'", Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
            Assert.Contains("/mobile/assets/index-Demo.js", await page.Content.ReadAsStringAsync(timeout.Token));
            using var asset = await client.GetAsync(new Uri(secure, "/mobile/assets/index-Demo.js"), timeout.Token);
            Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
            Assert.Equal("export {};", await asset.Content.ReadAsStringAsync(timeout.Token));

            foreach (var path in new[] { "/mobile/assets/example.txt", "/mobile/assets/missing.js", "/mobile/other", "/api/v1/absent" })
            {
                using var missing = await client.GetAsync(new Uri(secure, path), timeout.Token);
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            }
            using var plain = await client.GetAsync(new Uri(insecure, "/mobile/"), timeout.Token);
            Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        }
        finally
        {
            File.Delete(index);
            File.Delete(script);
            File.Delete(unsupported);
            assets.Delete();
            root.Delete();
        }
    }

    private static IConfiguration Configuration(string path) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BranchMobileHosting:Enabled"] = "true",
            ["BranchMobileHosting:BuildDirectory"] = path
        }).Build();

    internal static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Branch loopback", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddHours(-1), now.AddHours(1));
        var pkcs12 = certificate.Export(X509ContentType.Pkcs12);
        try { return X509CertificateLoader.LoadPkcs12(pkcs12, null); }
        finally { CryptographicOperations.ZeroMemory(pkcs12); }
    }
}
