using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralBranchCertificateAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "BranchCertificate";
    public const string RegistryPath = "CentralBranchMtls:Certificates";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.IsHttps) { return AuthenticateResult.NoResult(); }
        var certificate = await Context.Connection.GetClientCertificateAsync(Context.RequestAborted);
        if (certificate is null) { return AuthenticateResult.NoResult(); }

        var now = clock.GetUtcNow().UtcDateTime;
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime()
            || !certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Any(extension => extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.2")))
        {
            return AuthenticateResult.Fail("Branch client certificate is invalid.");
        }

        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var matches = configuration.GetSection(RegistryPath).GetChildren()
            .Where(entry => string.Equals(entry["Sha256"], fingerprint, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matches.Length != 1 || !bool.TryParse(matches[0]["Enabled"], out var enabled) || !enabled
            || matches[0]["BranchId"] is not { Length: > 0 } branchId || branchId != branchId.Trim())
        {
            return AuthenticateResult.Fail("Branch client certificate is not registered.");
        }

        var identity = new ClaimsIdentity([new Claim(CentralCloudClaimTypes.BranchId, branchId)], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
