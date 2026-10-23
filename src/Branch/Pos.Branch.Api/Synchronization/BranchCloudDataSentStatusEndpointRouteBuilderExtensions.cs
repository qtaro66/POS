using Microsoft.AspNetCore.Mvc;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudDataSentStatusEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/cloud-data-sent-status";

    public const string EndpointName = "GetBranchCloudDataSentStatus";

    public const string AuthorizationPolicyName =
        BranchPosAuthorizationDefaults.CompleteIdentityPolicyName;

    public const long MaxRequestBodySize = 0;

    public static RouteHandlerBuilder MapBranchCloudDataSentStatusEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapGet(
                RoutePattern,
                BranchCloudDataSentStatusPrincipalHttpHandler.HandleAsync)
            .RequireAuthorization(AuthorizationPolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }
}
