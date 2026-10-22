namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionHttpsApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBranchEmployeeSessionHttpsOnly(
        this IApplicationBuilder application,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(BranchEmployeeSessionOptions.SectionName)
            .GetValue<bool>(nameof(BranchEmployeeSessionOptions.Enabled));
        if (!enabled)
        {
            return application;
        }

        return application.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps &&
                context.Request.Path.StartsWithSegments(
                    BranchEmployeeSessionLoginEndpointRouteBuilderExtensions.RoutePattern))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }

            await next(context);
        });
    }
}
