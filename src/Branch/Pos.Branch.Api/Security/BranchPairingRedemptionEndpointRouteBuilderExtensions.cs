using Microsoft.AspNetCore.Mvc;

namespace Pos.Branch.Api.Security;

public static class BranchPairingRedemptionEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/pairing/redemptions";

    public const string EndpointName = "RedeemBranchPairing";

    public const long MaxRequestBodySize = 4 * 1024;

    public static RouteHandlerBuilder MapBranchPairingRedemptionEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapPost(RoutePattern, BranchPairingRedemptionHttpHandler.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(BranchPairingRedemptionRateLimitPolicy.PolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }

    public static IEndpointRouteBuilder MapBranchPairingRedemptionEndpointIfEnabled(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(BranchPairingOptions.SectionName)
            .GetValue<bool>(nameof(BranchPairingOptions.Enabled));
        if (enabled)
        {
            endpoints.MapBranchPairingRedemptionEndpoint();
        }

        return endpoints;
    }
}
