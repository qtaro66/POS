using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class CentralBranchCertificateAuthenticationTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    public async Task Issues_branch_claim_only_for_registered_https_client_certificate(
        bool https, bool enabled, bool clientUsage, bool accepted)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Example branch", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(clientUsage ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, false));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddHours(-1), now.AddHours(1));
        var registry = CentralBranchCertificateAuthentication.RegistryPath + ":0:";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [registry + "Sha256"] = certificate.GetCertHashString(HashAlgorithmName.SHA256),
            [registry + "BranchId"] = "BRANCH-EXAMPLE",
            [registry + "Enabled"] = enabled.ToString()
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, CentralBranchCertificateAuthentication>(
            CentralBranchCertificateAuthentication.SchemeName, _ => { });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Scheme = https ? "https" : "http";
        context.Connection.ClientCertificate = certificate;
        context.Request.Headers["X-Branch-Id"] = "UNTRUSTED";
        var result = await context.AuthenticateAsync(CentralBranchCertificateAuthentication.SchemeName);
        Assert.Equal(accepted, result.Succeeded);
        Assert.Equal(accepted ? "BRANCH-EXAMPLE" : null, result.Principal?.FindFirst(CentralCloudClaimTypes.BranchId)?.Value);
    }
}
