using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchPairingRedemptionHttpHandler
{
    public static async Task<Results<
        Ok<BranchPairingRedemptionResponse>,
        BadRequest,
        UnauthorizedHttpResult>> HandleAsync(
        BranchPairingRedemptionRequest request,
        BranchPairingRedemptionService redemptionService,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        BranchPairingSecretResponseHeaders.Apply(httpResponse);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(redemptionService);

        if (request.SchemaVersion != BranchPairingRedemptionResponseMapper.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(request.BranchId) ||
            string.IsNullOrWhiteSpace(request.DeviceDisplayName))
        {
            return TypedResults.BadRequest();
        }

        var redeemed = await redemptionService.RedeemAsync(
            request.BranchId,
            request.PairingId,
            request.OneTimeToken,
            request.DeviceDisplayName,
            cancellationToken);
        return redeemed is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(BranchPairingRedemptionResponseMapper.Map(redeemed));
    }
}
