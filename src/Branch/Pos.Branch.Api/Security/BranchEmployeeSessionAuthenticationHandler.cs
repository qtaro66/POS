using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Pos.Application.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionAuthenticationDefaults
{
    public const string SchemeName = "BranchEmployeeSession";

    public const string HeaderName = "X-POS-Employee-Session";
}

public sealed class BranchEmployeeSessionAuthenticationHandler :
    AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string FailureMessage = "Invalid Branch employee session.";

    private readonly BranchEmployeeSessionAuthenticationService _authentication;

    public BranchEmployeeSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        BranchEmployeeSessionAuthenticationService authentication)
        : base(options, logger, encoder)
    {
        _authentication = authentication ??
            throw new ArgumentNullException(nameof(authentication));
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(
                BranchEmployeeSessionAuthenticationDefaults.HeaderName,
                out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        if (headerValues.Count != 1 ||
            string.IsNullOrEmpty(headerValues[0]))
        {
            return AuthenticateResult.Fail(FailureMessage);
        }

        var deviceResult = await Context.AuthenticateAsync(
            BranchDeviceAuthenticationDefaults.SchemeName);
        if (!deviceResult.Succeeded ||
            !AuthenticatedBranchDeviceIdentityResolver.TryResolve(
                deviceResult.Principal,
                out var device))
        {
            return AuthenticateResult.Fail(FailureMessage);
        }

        var session = await _authentication.AuthenticateAsync(
            device.BranchId,
            device.DeviceId,
            headerValues[0]!.AsMemory(),
            Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail(FailureMessage);
        }

        var claims = new[]
        {
            new Claim(BranchPosClaimTypes.BranchId, session.BranchId),
            new Claim(BranchPosClaimTypes.DeviceId, session.DeviceId),
            new Claim(BranchPosClaimTypes.EmployeeId, session.EmployeeId)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }
}
