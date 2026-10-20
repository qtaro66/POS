using Microsoft.AspNetCore.Mvc;

namespace Pos.Branch.Api.Security;

public static class BranchPairingIssuanceEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/pairing/challenges";

    public const string EndpointName = "IssueBranchPairingChallenge";

    public const string AuthorizationPolicyName = "BranchPosAuthenticated";

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
}
