namespace Pos.Central.Api.Synchronization;

public static class BranchCloudEventIngestionHttpsApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBranchCloudEventIngestionHttpsOnly(
        this IApplicationBuilder application,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(CentralCloudIngestionOptions.SectionName)
            .GetValue<bool>(nameof(CentralCloudIngestionOptions.Enabled));
        if (!enabled)
        {
            return application;
        }

        return application.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps &&
                context.Request.Path.StartsWithSegments(
                    BranchCloudEventIngestionEndpointRouteBuilderExtensions
                        .RoutePattern))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }

            await next(context);
        });
    }
}
