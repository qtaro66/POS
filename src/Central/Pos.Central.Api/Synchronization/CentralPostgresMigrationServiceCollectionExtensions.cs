using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public static class CentralPostgresMigrationServiceCollectionExtensions
{
    public const string ConnectionStringName = "CentralDatabase";

    public static IServiceCollection AddCentralPostgresMigrationsIfEnabled(
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

        var connectionString = configuration.GetConnectionString(
            ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is required when " +
                "Central Cloud ingestion is enabled.");
        }

        services.AddPostgresCentralMigrations(connectionString);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService,
                CentralPostgresMigrationStartupService>());
        return services;
    }
}
