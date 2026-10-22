using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionLoginPrincipalHttpHandler
{
    public static async Task<Results<
        Ok<BranchEmployeeSessionLoginResponse>,
        UnauthorizedHttpResult>> HandleAsync(
        ClaimsPrincipal? principal,
        ReadOnlyMemory<char> presentedPin,
        BranchEmployeeSessionLoginService loginService,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        BranchSecretResponseHeaders.Apply(httpResponse);
        ArgumentNullException.ThrowIfNull(loginService);

        if (!AuthenticatedBranchDeviceIdentityResolver.TryResolve(
                principal,
                out var device))
        {
            return TypedResults.Unauthorized();
        }

        return await BranchEmployeeSessionLoginHttpHandler.HandleAsync(
            device,
            presentedPin,
            loginService,
            httpResponse,
            cancellationToken);
    }
}
