using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public static class CentralPostgresCloudIngestionServiceCollectionExtensions
{
    public static IServiceCollection AddCentralPostgresCloudIngestionIfEnabled(
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
            CentralPostgresMigrationServiceCollectionExtensions
                .ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{CentralPostgresMigrationServiceCollectionExtensions.ConnectionStringName} " +
                "is required when Central Cloud ingestion is enabled.");
        }

        services.AddPostgresBranchCloudEventIngestion(connectionString);
        return services;
    }
}
