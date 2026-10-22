using Microsoft.AspNetCore.Mvc;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionLoginEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/api/v1/employee-sessions";

    public const string EndpointName = "CreateBranchEmployeeSession";

    public const string AuthorizationPolicyName =
        BranchDeviceAuthenticationDefaults.AuthorizationPolicyName;

    public const long MaxRequestBodySize = 4 * 1024;

    public static RouteHandlerBuilder MapBranchEmployeeSessionLoginEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapPost(
                RoutePattern,
                BranchEmployeeSessionLoginRequestHttpHandler.HandleAsync)
            .RequireAuthorization(AuthorizationPolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodySize))
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }
}
