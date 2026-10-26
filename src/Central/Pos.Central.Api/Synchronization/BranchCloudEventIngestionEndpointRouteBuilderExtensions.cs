using Microsoft.AspNetCore.Mvc;

namespace Pos.Central.Api.Synchronization;

public static class CentralCloudIngestionAuthorizationDefaults
{
    public const string PolicyName = "CentralCloudIngestion";
}

public static class BranchCloudEventIngestionEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/branch-events";

    public const string EndpointName = "IngestBranchCloudEvent";

    public const string AuthorizationPolicyName =
        CentralCloudIngestionAuthorizationDefaults.PolicyName;

    public const long MaxRequestBodySize = 16 * 1024;

    public static RouteHandlerBuilder MapBranchCloudEventIngestionEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapPost(
                RoutePattern,
                BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync)
            .RequireAuthorization(AuthorizationPolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }
}
