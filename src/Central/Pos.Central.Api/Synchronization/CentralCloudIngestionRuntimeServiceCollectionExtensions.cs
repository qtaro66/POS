using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pos.Central.Api.Synchronization;

public static class CentralCloudIngestionRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddCentralCloudIngestionRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCentralCloudIngestionRuntimeConfiguration(configuration);
        services.AddCentralPostgresMigrationsIfEnabled(configuration);
        services.AddCentralPostgresCloudIngestionIfEnabled(configuration);
        services.AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
            configuration);
        return services;
    }

    public static IServiceCollection AddCentralCloudIngestionRuntimeConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(
            CentralCloudIngestionOptions.SectionName);
        services
            .AddOptions<CentralCloudIngestionOptions>()
            .Bind(section)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<CentralCloudIngestionOptions>,
                CentralCloudIngestionOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<CentralCloudIngestionOptions>,
                CentralCloudIngestionAuthenticationSchemeValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<CentralCloudIngestionOptions>,
                CentralCloudIngestionPersistenceReadinessValidator>());

        var enabled = section.GetValue<bool>(
            nameof(CentralCloudIngestionOptions.Enabled));
        var authenticationScheme = section.GetValue<string>(
            nameof(CentralCloudIngestionOptions.AuthenticationScheme));
        if (enabled &&
            CentralCloudIngestionOptionsValidator
                .IsCanonicalAuthenticationScheme(authenticationScheme))
        {
            services.AddCentralCloudIngestionAuthorization(authenticationScheme!);
        }

        return services;
    }
}
