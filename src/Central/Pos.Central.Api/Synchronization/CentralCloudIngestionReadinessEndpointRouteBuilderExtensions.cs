using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pos.Central.Api.Synchronization;

public static class
    CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
{
    public const string RoutePattern = "/health/ready";

    public const string EndpointName = "CentralCloudIngestionReadiness";

    public static IEndpointConventionBuilder
        MapCentralCloudIngestionReadinessEndpoint(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapHealthChecks(
                RoutePattern,
                new HealthCheckOptions
                {
                    AllowCachingResponses = false,
                    Predicate = registration =>
                        registration.Tags.Contains(
                            CentralCloudIngestionReadinessHealthCheck.Tag),
                    ResponseWriter = WriteMinimalPlaintextAsync
                })
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName(EndpointName);
    }

    public static IEndpointRouteBuilder
        MapCentralCloudIngestionReadinessEndpointIfEnabled(
            this IEndpointRouteBuilder endpoints,
            IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(CentralCloudIngestionOptions.SectionName)
            .GetValue<bool>(nameof(CentralCloudIngestionOptions.Enabled));
        if (enabled)
        {
            endpoints.MapCentralCloudIngestionReadinessEndpoint();
        }

        return endpoints;
    }

    private static Task WriteMinimalPlaintextAsync(
        HttpContext context,
        HealthReport report)
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(report.Status.ToString());
    }
}
