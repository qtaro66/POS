using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pos.Central.Persistence;

public static class PostgresCentralMigrationServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresCentralMigrations(
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
            NpgsqlPostgresCentralMigrationSessionFactory>();
        services.TryAddSingleton<IPostgresCentralMigrationSessionFactory>(
            provider => provider.GetRequiredService<
                NpgsqlPostgresCentralMigrationSessionFactory>());
        services.TryAddSingleton<PostgresCentralMigrationExecutor>();
        return services;
    }
}
