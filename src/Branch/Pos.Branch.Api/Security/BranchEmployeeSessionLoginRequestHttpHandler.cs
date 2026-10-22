using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionLoginRequestHttpHandler
{
    public static async Task<Results<
        Ok<BranchEmployeeSessionLoginResponse>,
        BadRequest,
        UnauthorizedHttpResult>> HandleAsync(
        ClaimsPrincipal? principal,
        BranchEmployeeSessionLoginRequest request,
        BranchEmployeeSessionLoginService loginService,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        BranchSecretResponseHeaders.Apply(httpResponse);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loginService);

        if (request.SchemaVersion !=
                BranchEmployeeSessionLoginResponseMapper.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(request.Pin))
        {
            return TypedResults.BadRequest();
        }

        var result = await BranchEmployeeSessionLoginPrincipalHttpHandler.HandleAsync(
            principal,
            request.Pin.AsMemory(),
            loginService,
            httpResponse,
            cancellationToken);

        return result.Result switch
        {
            Ok<BranchEmployeeSessionLoginResponse> ok => ok,
            UnauthorizedHttpResult unauthorized => unauthorized,
            _ => throw new InvalidOperationException(
                "The employee-session login handler returned an unexpected result.")
        };
    }
}
