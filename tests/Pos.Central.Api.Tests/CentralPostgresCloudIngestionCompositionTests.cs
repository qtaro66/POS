using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class CentralPostgresCloudIngestionCompositionTests
{
    private const string OfflineConnectionString =
        "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";

    [Fact]
    public void Disabled_ingestion_registers_no_persistence_services()
    {
        var services = new ServiceCollection();

        var returned = services.AddCentralPostgresCloudIngestionIfEnabled(
            Configuration(enabled: false, connectionString: null));

        Assert.Same(services, returned);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(PostgresCentralConnectionFactory));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(IPostgresCloudIngestionReadinessProbe));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(IBranchCloudEventIngestionStore));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(BranchCloudEventIngestionService));
    }

    [Fact]
    public async Task Enabled_ingestion_registers_offline_persistence_runtime()
    {
        var services = new ServiceCollection();
        services.AddCentralPostgresCloudIngestionIfEnabled(
            Configuration(
                enabled: true,
                connectionString: OfflineConnectionString));
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<
            PostgresCentralConnectionFactory>());
        Assert.IsType<NpgsqlPostgresCloudIngestionReadinessProbe>(
            provider.GetRequiredService<
                IPostgresCloudIngestionReadinessProbe>());
        Assert.NotNull(provider.GetRequiredService<
            IPostgresBranchCloudEventIngestionSessionFactory>());
        Assert.IsType<PostgresBranchCloudEventIngestionStore>(
            provider.GetRequiredService<IBranchCloudEventIngestionStore>());
        Assert.NotNull(provider.GetRequiredService<
            BranchCloudEventIngestionService>());
    }

    [Fact]
    public async Task Migration_and_ingestion_configuration_share_runtime()
    {
        var configuration = Configuration(
            enabled: true,
            connectionString: OfflineConnectionString);
        var services = new ServiceCollection();
        services.AddCentralPostgresMigrationsIfEnabled(configuration);
        services.AddCentralPostgresCloudIngestionIfEnabled(configuration);
        await using var provider = services.BuildServiceProvider();

        Assert.Single(
            provider.GetServices<PostgresCentralConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<
            PostgresCentralMigrationExecutor>());
        Assert.NotNull(provider.GetRequiredService<
            IPostgresCloudIngestionReadinessProbe>());
        Assert.NotNull(provider.GetRequiredService<
            BranchCloudEventIngestionService>());
    }

    [Fact]
    public void Repeated_enabled_registration_is_idempotent()
    {
        var configuration = Configuration(
            enabled: true,
            connectionString: OfflineConnectionString);
        var services = new ServiceCollection();

        services.AddCentralPostgresCloudIngestionIfEnabled(configuration);
        services.AddCentralPostgresCloudIngestionIfEnabled(configuration);

        Assert.Single(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(IBranchCloudEventIngestionStore));
        Assert.Single(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(IPostgresCloudIngestionReadinessProbe));
        Assert.Single(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(BranchCloudEventIngestionService));
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
            services.AddCentralPostgresCloudIngestionIfEnabled(
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
            CentralPostgresCloudIngestionServiceCollectionExtensions
                .AddCentralPostgresCloudIngestionIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCentralPostgresCloudIngestionIfEnabled(null!));
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
