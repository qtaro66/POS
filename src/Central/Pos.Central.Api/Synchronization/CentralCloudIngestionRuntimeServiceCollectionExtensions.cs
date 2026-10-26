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

        services
            .AddOptions<CentralCloudIngestionOptions>()
            .Bind(configuration.GetSection(CentralCloudIngestionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<CentralCloudIngestionOptions>,
                CentralCloudIngestionOptionsValidator>());
        return services;
    }
}
