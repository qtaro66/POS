using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pos.Application.Synchronization;

namespace Pos.Central.Persistence;

public static class PostgresBranchCloudEventIngestionServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresBranchCloudEventIngestion(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "The Central PostgreSQL connection string cannot be empty.",
                nameof(connectionString));
        }

        services.TryAddSingleton(_ =>
            new PostgresCentralConnectionFactory(connectionString));
        services.TryAddSingleton<
            NpgsqlPostgresCloudIngestionReadinessProbe>();
        services.TryAddSingleton<IPostgresCloudIngestionReadinessProbe>(
            provider => provider.GetRequiredService<
                NpgsqlPostgresCloudIngestionReadinessProbe>());
        services.TryAddSingleton<
            NpgsqlPostgresBranchCloudEventIngestionSessionFactory>();
        services.TryAddSingleton<
            IPostgresBranchCloudEventIngestionSessionFactory>(
                provider => provider.GetRequiredService<
                    NpgsqlPostgresBranchCloudEventIngestionSessionFactory>());
        services.TryAddSingleton<PostgresBranchCloudEventIngestionStore>();
        services.TryAddSingleton<IBranchCloudEventIngestionStore>(
            provider => provider.GetRequiredService<
                PostgresBranchCloudEventIngestionStore>());
        services.TryAddSingleton<BranchCloudEventIngestionService>();
        return services;
    }
}
