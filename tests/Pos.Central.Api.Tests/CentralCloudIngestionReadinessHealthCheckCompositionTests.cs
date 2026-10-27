using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class
    CentralCloudIngestionReadinessHealthCheckCompositionTests
{
    private const string OfflineConnectionString =
        "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";

    [Fact]
    public void Disabled_ingestion_registers_no_readiness_health_check()
    {
        var services = new ServiceCollection();

        var returned = services
            .AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
                Configuration(enabled: false));

        Assert.Same(services, returned);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(CentralCloudIngestionReadinessHealthCheck));
    }

    [Fact]
    public async Task Enabled_runtime_registers_one_resolvable_readiness_check()
    {
        var configuration = Configuration(enabled: true);
        var services = new ServiceCollection();
        services.AddCentralPostgresCloudIngestionIfEnabled(configuration);
        services.AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
            configuration);
        await using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;
        var registration = Assert.Single(
            options.Registrations,
            item => item.Name ==
                CentralCloudIngestionReadinessHealthCheck.Name);
        var concrete = provider.GetRequiredService<
            CentralCloudIngestionReadinessHealthCheck>();

        Assert.Same(concrete, registration.Factory(provider));
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Contains(
            CentralCloudIngestionReadinessHealthCheck.Tag,
            registration.Tags);
    }

    [Fact]
    public async Task Repeated_enabled_registration_is_idempotent()
    {
        var configuration = Configuration(enabled: true);
        var services = new ServiceCollection();
        services.AddCentralPostgresCloudIngestionIfEnabled(configuration);
        services.AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
            configuration);
        services.AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
            configuration);
        await using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;

        Assert.Single(
            options.Registrations,
            item => item.Name ==
                CentralCloudIngestionReadinessHealthCheck.Name);
        Assert.Single(
            provider.GetServices<
                CentralCloudIngestionReadinessHealthCheck>());
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(enabled: false);

        Assert.Throws<ArgumentNullException>(() =>
            CentralCloudIngestionReadinessHealthCheckServiceCollectionExtensions
                .AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCentralCloudIngestionReadinessHealthCheckIfEnabled(
                null!));
    }

    private static IConfiguration Configuration(bool enabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["CentralCloudIngestion:Enabled"] = enabled.ToString()
        };
        if (enabled)
        {
            values["ConnectionStrings:CentralDatabase"] =
                OfflineConnectionString;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
