using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresBranchCloudEventIngestionCompositionTests
{
    private const string OfflineConnectionString =
        "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";

    [Fact]
    public async Task Registers_complete_ingestion_runtime_without_network_access()
    {
        var services = new ServiceCollection();
        services.AddPostgresBranchCloudEventIngestion(OfflineConnectionString);
        await using var provider = services.BuildServiceProvider();

        var connectionFactory = provider.GetRequiredService<
            PostgresCentralConnectionFactory>();
        var concreteSessionFactory = provider.GetRequiredService<
            NpgsqlPostgresBranchCloudEventIngestionSessionFactory>();
        var sessionFactory = provider.GetRequiredService<
            IPostgresBranchCloudEventIngestionSessionFactory>();
        var concreteStore = provider.GetRequiredService<
            PostgresBranchCloudEventIngestionStore>();
        var store = provider.GetRequiredService<
            IBranchCloudEventIngestionStore>();
        var service = provider.GetRequiredService<
            BranchCloudEventIngestionService>();

        Assert.Equal(
            PostgresCentralConnectionFactory.DefaultApplicationName,
            connectionFactory.ApplicationName);
        Assert.Same(concreteSessionFactory, sessionFactory);
        Assert.Same(concreteStore, store);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task Repeated_registration_is_singleton_and_idempotent()
    {
        var services = new ServiceCollection();
        services.AddPostgresBranchCloudEventIngestion(OfflineConnectionString);
        services.AddPostgresBranchCloudEventIngestion(OfflineConnectionString);
        await using var provider = services.BuildServiceProvider();

        Assert.Single(
            provider.GetServices<PostgresCentralConnectionFactory>());
        Assert.Single(
            provider.GetServices<
                NpgsqlPostgresBranchCloudEventIngestionSessionFactory>());
        Assert.Single(
            provider.GetServices<
                IPostgresBranchCloudEventIngestionSessionFactory>());
        Assert.Single(
            provider.GetServices<PostgresBranchCloudEventIngestionStore>());
        Assert.Single(
            provider.GetServices<IBranchCloudEventIngestionStore>());
        Assert.Single(
            provider.GetServices<BranchCloudEventIngestionService>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Migration_and_ingestion_composition_share_connection_factory(
        bool migrationsFirst)
    {
        var services = new ServiceCollection();
        if (migrationsFirst)
        {
            services.AddPostgresCentralMigrations(OfflineConnectionString);
            services.AddPostgresBranchCloudEventIngestion(
                OfflineConnectionString);
        }
        else
        {
            services.AddPostgresBranchCloudEventIngestion(
                OfflineConnectionString);
            services.AddPostgresCentralMigrations(OfflineConnectionString);
        }

        await using var provider = services.BuildServiceProvider();

        Assert.Single(
            provider.GetServices<PostgresCentralConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<
            PostgresCentralMigrationExecutor>());
        Assert.NotNull(provider.GetRequiredService<
            BranchCloudEventIngestionService>());
    }

    [Fact]
    public void Returns_supplied_service_collection()
    {
        var services = new ServiceCollection();

        var returned = services.AddPostgresBranchCloudEventIngestion(
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
            services.AddPostgresBranchCloudEventIngestion(
                connectionString!));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void Missing_service_collection_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PostgresBranchCloudEventIngestionServiceCollectionExtensions
                .AddPostgresBranchCloudEventIngestion(
                    null!,
                    OfflineConnectionString));
    }
}
