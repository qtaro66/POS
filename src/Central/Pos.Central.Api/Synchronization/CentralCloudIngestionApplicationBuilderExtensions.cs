namespace Pos.Central.Api.Synchronization;

public static class CentralCloudIngestionApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCentralCloudIngestionSecurityIfEnabled(
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

        application.UseAuthentication();
        application.UseAuthorization();
        return application;
    }
}
