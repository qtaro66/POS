namespace Pos.Branch.Api.Security;

public static class BranchPairingHttpsApplicationBuilderExtensions
{
    public static readonly PathString PairingPathPrefix = "/api/v1/pairing";

    public static IApplicationBuilder UseBranchPairingHttpsOnly(
        this IApplicationBuilder application,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(BranchPairingOptions.SectionName)
            .GetValue<bool>(nameof(BranchPairingOptions.Enabled));
        if (!enabled)
        {
            return application;
        }

        return application.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps &&
                context.Request.Path.StartsWithSegments(PairingPathPrefix))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }

            await next(context);
        });
    }
}
