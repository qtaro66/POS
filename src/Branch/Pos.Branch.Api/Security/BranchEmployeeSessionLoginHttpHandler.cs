using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionLoginHttpHandler
{
    public static async Task<Results<
        Ok<BranchEmployeeSessionLoginResponse>,
        UnauthorizedHttpResult>> HandleAsync(
        VerifiedBranchDevice device,
        ReadOnlyMemory<char> presentedPin,
        BranchEmployeeSessionLoginService loginService,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        BranchSecretResponseHeaders.Apply(httpResponse);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(loginService);

        var login = await loginService.LoginAsync(
            device.BranchId,
            device.DeviceId,
            presentedPin,
            cancellationToken);

        return login is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(BranchEmployeeSessionLoginResponseMapper.Map(login));
    }
}
