using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pos.Central.Api.Synchronization;

public static class
    CentralCloudIngestionReadinessHealthCheckServiceCollectionExtensions
{
    public static IServiceCollection
        AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
            this IServiceCollection services,
            IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(CentralCloudIngestionOptions.SectionName)
            .GetValue<bool>(nameof(CentralCloudIngestionOptions.Enabled));
        if (!enabled)
        {
            return services;
        }

        services.AddLogging();
        services.AddOptions<HealthCheckServiceOptions>();
        services.AddHealthChecks();
        services.TryAddSingleton<CentralCloudIngestionReadinessHealthCheck>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IConfigureOptions<HealthCheckServiceOptions>,
                CentralCloudIngestionReadinessHealthCheckOptionsSetup>());
        return services;
    }

    private sealed class CentralCloudIngestionReadinessHealthCheckOptionsSetup :
        IConfigureOptions<HealthCheckServiceOptions>
    {
        public void Configure(HealthCheckServiceOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Registrations.Any(registration =>
                string.Equals(
                    registration.Name,
                    CentralCloudIngestionReadinessHealthCheck.Name,
                    StringComparison.Ordinal)))
            {
                return;
            }

            options.Registrations.Add(
                new HealthCheckRegistration(
                    CentralCloudIngestionReadinessHealthCheck.Name,
                    provider => provider.GetRequiredService<
                        CentralCloudIngestionReadinessHealthCheck>(),
                    HealthStatus.Unhealthy,
                    [CentralCloudIngestionReadinessHealthCheck.Tag]));
        }
    }
}
