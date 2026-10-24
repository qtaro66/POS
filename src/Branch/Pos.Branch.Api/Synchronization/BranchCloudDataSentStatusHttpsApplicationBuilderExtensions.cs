namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudDataSentStatusHttpsApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBranchCloudDataSentStatusHttpsOnly(
        this IApplicationBuilder application,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(BranchCloudDataSentStatusOptions.SectionName)
            .GetValue<bool>(nameof(BranchCloudDataSentStatusOptions.Enabled));
        if (!enabled)
        {
            return application;
        }

        return application.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps &&
                context.Request.Path.StartsWithSegments(
                    BranchCloudDataSentStatusEndpointRouteBuilderExtensions.RoutePattern))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }

            await next(context);
        });
    }
}
