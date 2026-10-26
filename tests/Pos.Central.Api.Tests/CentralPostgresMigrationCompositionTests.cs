using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class CentralPostgresMigrationCompositionTests
{
    private const string OfflineConnectionString =
        "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";

    [Fact]
    public void Disabled_ingestion_registers_no_postgres_services()
    {
        var services = new ServiceCollection();

        var returned = services.AddCentralPostgresMigrationsIfEnabled(
            Configuration(enabled: false, connectionString: null));

        Assert.Same(services, returned);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(PostgresCentralConnectionFactory));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType ==
                    typeof(CentralPostgresMigrationStartupService));
    }

    [Fact]
    public async Task Enabled_ingestion_registers_offline_migration_runtime()
    {
        var services = new ServiceCollection();
        services.AddCentralPostgresMigrationsIfEnabled(
            Configuration(
                enabled: true,
                connectionString: OfflineConnectionString));
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<
            PostgresCentralConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<
            IPostgresCentralMigrationSessionFactory>());
        Assert.NotNull(provider.GetRequiredService<
            PostgresCentralMigrationExecutor>());
        Assert.IsType<CentralPostgresMigrationStartupService>(
            Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Fact]
    public void Repeated_enabled_registration_adds_one_startup_service()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(
            enabled: true,
            connectionString: OfflineConnectionString);

        services.AddCentralPostgresMigrationsIfEnabled(configuration);
        services.AddCentralPostgresMigrationsIfEnabled(configuration);

        Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType ==
                    typeof(CentralPostgresMigrationStartupService));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Enabled_ingestion_requires_named_connection_string(
        string? connectionString)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCentralPostgresMigrationsIfEnabled(
                Configuration(enabled: true, connectionString)));

        Assert.Contains(
            "ConnectionStrings:CentralDatabase",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(enabled: false, connectionString: null);

        Assert.Throws<ArgumentNullException>(() =>
            CentralPostgresMigrationServiceCollectionExtensions
                .AddCentralPostgresMigrationsIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCentralPostgresMigrationsIfEnabled(null!));
    }

    private static IConfiguration Configuration(
        bool enabled,
        string? connectionString)
    {
        var values = new Dictionary<string, string?>
        {
            ["CentralCloudIngestion:Enabled"] = enabled.ToString()
        };
        if (connectionString is not null)
        {
            values["ConnectionStrings:CentralDatabase"] = connectionString;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
