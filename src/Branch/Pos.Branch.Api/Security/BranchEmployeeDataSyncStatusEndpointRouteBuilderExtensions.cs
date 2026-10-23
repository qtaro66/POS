using Microsoft.AspNetCore.Mvc;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/employee-data-sync-status";

    public const string EndpointName = "GetBranchEmployeeDataSyncStatus";

    public const string AuthorizationPolicyName =
        BranchPosAuthorizationDefaults.CompleteIdentityPolicyName;

    public const long MaxRequestBodySize = 0;

    public static RouteHandlerBuilder MapBranchEmployeeDataSyncStatusEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapGet(
                RoutePattern,
                BranchEmployeeDataSyncStatusPrincipalHttpHandler.HandleAsync)
            .RequireAuthorization(AuthorizationPolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }
}
