namespace Pos.Branch.Api.Security;

public static class BranchPairingRedemptionEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/pairing/redemptions";

    public const string EndpointName = "RedeemBranchPairing";

    public static RouteHandlerBuilder MapBranchPairingRedemptionEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapPost(RoutePattern, BranchPairingRedemptionHttpHandler.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(BranchPairingRedemptionRateLimitPolicy.PolicyName)
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }
}
