namespace Pos.Central.Api.Synchronization;

public static class BranchCloudEventIngestionHttpsApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBranchCloudEventIngestionHttpsOnly(
        this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

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
