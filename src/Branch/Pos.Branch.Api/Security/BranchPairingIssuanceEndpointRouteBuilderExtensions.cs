using Microsoft.AspNetCore.Mvc;

namespace Pos.Branch.Api.Security;

public static class BranchPairingIssuanceEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/pairing/challenges";

    public const string EndpointName = "IssueBranchPairingChallenge";

    public const string AuthorizationPolicyName =
        BranchPosAuthorizationDefaults.CompleteIdentityPolicyName;

    public const long MaxRequestBodySize = 0;

    public static RouteHandlerBuilder MapBranchPairingIssuanceEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapPost(RoutePattern, BranchPairingIssuancePrincipalHttpHandler.HandleAsync)
            .RequireAuthorization(AuthorizationPolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }

    public static IEndpointRouteBuilder MapBranchPairingIssuanceEndpointIfEnabled(
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
            endpoints.MapBranchPairingIssuanceEndpoint();
        }

        return endpoints;
    }
}
