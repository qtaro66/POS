using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pos.Central.Api.Synchronization;

public static class CentralCloudIngestionRuntimeServiceCollectionExtensions
{
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
