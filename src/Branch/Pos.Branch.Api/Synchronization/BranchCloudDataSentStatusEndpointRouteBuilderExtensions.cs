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

    public static IEndpointRouteBuilder MapBranchCloudDataSentStatusEndpointIfEnabled(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(BranchCloudDataSentStatusOptions.SectionName)
            .GetValue<bool>(nameof(BranchCloudDataSentStatusOptions.Enabled));
        if (enabled)
        {
            endpoints.MapBranchCloudDataSentStatusEndpoint();
        }

        return endpoints;
    }
}
