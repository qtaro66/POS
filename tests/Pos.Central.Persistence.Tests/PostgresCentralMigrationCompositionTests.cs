using Microsoft.Extensions.DependencyInjection;
using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresCentralMigrationCompositionTests
{
    private const string OfflineConnectionString =
        "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";

    [Fact]
    public async Task Registers_complete_migration_runtime_without_network_access()
    {
        var services = new ServiceCollection();
        services.AddPostgresCentralMigrations(OfflineConnectionString);
        await using var provider = services.BuildServiceProvider();

        var connectionFactory = provider.GetRequiredService<
            PostgresCentralConnectionFactory>();
        var concreteSessionFactory = provider.GetRequiredService<
            NpgsqlPostgresCentralMigrationSessionFactory>();
        var sessionFactory = provider.GetRequiredService<
            IPostgresCentralMigrationSessionFactory>();
        var executor = provider.GetRequiredService<
            PostgresCentralMigrationExecutor>();

        Assert.Equal(
            PostgresCentralConnectionFactory.DefaultApplicationName,
            connectionFactory.ApplicationName);
        Assert.Same(concreteSessionFactory, sessionFactory);
        Assert.NotNull(executor);
    }

    [Fact]
    public async Task Repeated_registration_remains_singleton_and_idempotent()
    {
        var services = new ServiceCollection();
        services.AddPostgresCentralMigrations(OfflineConnectionString);
        services.AddPostgresCentralMigrations(OfflineConnectionString);
        await using var provider = services.BuildServiceProvider();

        Assert.Single(
            provider.GetServices<PostgresCentralConnectionFactory>());
        Assert.Single(
            provider.GetServices<
                NpgsqlPostgresCentralMigrationSessionFactory>());
        Assert.Single(
            provider.GetServices<IPostgresCentralMigrationSessionFactory>());
        Assert.Single(
            provider.GetServices<PostgresCentralMigrationExecutor>());
    }

    [Fact]
    public void Returns_supplied_service_collection()
    {
        var services = new ServiceCollection();

        var returned = services.AddPostgresCentralMigrations(
            OfflineConnectionString);

        Assert.Same(services, returned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_connection_string_is_rejected(string? connectionString)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddPostgresCentralMigrations(connectionString!));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void Missing_service_collection_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PostgresCentralMigrationServiceCollectionExtensions
                .AddPostgresCentralMigrations(
                    null!,
                    OfflineConnectionString));
    }
}
