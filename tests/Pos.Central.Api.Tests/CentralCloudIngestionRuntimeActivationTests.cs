using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class CentralCloudIngestionRuntimeActivationTests
{
    private const string AuthenticationScheme = "CentralBranchCertificate";

    private const string OfflineConnectionString =
        "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";

    [Fact]
    public void Disabled_runtime_validates_without_postgres_services()
    {
        var services = new ServiceCollection();
        services.AddCentralCloudIngestionRuntime(Configuration(enabled: false));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Null(provider.GetService<PostgresCentralConnectionFactory>());
        Assert.Null(provider.GetService<IBranchCloudEventIngestionStore>());
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service is CentralPostgresMigrationStartupService);
    }

    [Fact]
    public async Task Enabled_runtime_composes_validated_postgres_ingestion()
    {
        var services = new ServiceCollection();
        services
            .AddAuthentication()
            .AddPolicyScheme(
                AuthenticationScheme,
                displayName: null,
                _ => { });
        services.AddCentralCloudIngestionRuntime(Configuration(enabled: true));
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.NotNull(provider.GetRequiredService<
            PostgresCentralConnectionFactory>());
        Assert.IsType<PostgresBranchCloudEventIngestionStore>(
            provider.GetRequiredService<IBranchCloudEventIngestionStore>());
        Assert.NotNull(provider.GetRequiredService<
            BranchCloudEventIngestionService>());
        Assert.IsType<CentralPostgresMigrationStartupService>(
            Assert.Single(
                provider.GetServices<IHostedService>(),
                service =>
                    service is CentralPostgresMigrationStartupService));
    }

    [Fact]
    public async Task Repeated_runtime_composition_remains_idempotent()
    {
        var configuration = Configuration(enabled: true);
        var services = new ServiceCollection();
        services
            .AddAuthentication()
            .AddPolicyScheme(
                AuthenticationScheme,
                displayName: null,
                _ => { });

        services.AddCentralCloudIngestionRuntime(configuration);
        services.AddCentralCloudIngestionRuntime(configuration);
        await using var provider = services.BuildServiceProvider();

        Assert.Single(
            provider.GetServices<IBranchCloudEventIngestionStore>());
        Assert.Single(
            provider.GetServices<BranchCloudEventIngestionService>());
        Assert.Single(
            provider.GetServices<IHostedService>(),
            service => service is CentralPostgresMigrationStartupService);
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var configuration = Configuration(enabled: false);
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            CentralCloudIngestionRuntimeServiceCollectionExtensions
                .AddCentralCloudIngestionRuntime(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCentralCloudIngestionRuntime(null!));
    }

    private static IConfiguration Configuration(bool enabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["CentralCloudIngestion:Enabled"] = enabled.ToString(),
            ["CentralCloudIngestion:AuthenticationScheme"] = enabled
                ? AuthenticationScheme
                : string.Empty
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
