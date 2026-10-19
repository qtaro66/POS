using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Pos.Application.Security;

namespace Pos.Branch.Api.Security;

public static class BranchDeviceAuthenticationDefaults
{
    public const string SchemeName = "BranchDevice";
    public const string AuthorizationScheme = "Device";
}

public sealed class BranchDeviceAuthenticationHandler :
    AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string FailureMessage = "Invalid Branch device credential.";

    private readonly BranchDeviceTokenAuthenticationService _authentication;

    public BranchDeviceAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        BranchDeviceTokenAuthenticationService authentication)
        : base(options, logger, encoder)
    {
        _authentication = authentication ??
            throw new ArgumentNullException(nameof(authentication));
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        if (headerValues.Count != 1 ||
            !TryReadToken(headerValues, out var token))
        {
            return AuthenticateResult.Fail(FailureMessage);
        }

        var device = await _authentication.AuthenticateAsync(
            token,
            Context.RequestAborted);
        if (device is null)
        {
            return AuthenticateResult.Fail(FailureMessage);
        }

        var claims = new[]
        {
            new Claim(BranchPosClaimTypes.BranchId, device.BranchId),
            new Claim(BranchPosClaimTypes.DeviceId, device.DeviceId)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }

    private static bool TryReadToken(
        StringValues headerValues,
        out string token)
    {
        token = string.Empty;
        var header = headerValues[0];
        if (header is null)
        {
            return false;
        }

        var prefix = $"{BranchDeviceAuthenticationDefaults.AuthorizationScheme} ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = header[prefix.Length..];
        return token.Length > 0;
    }
}
