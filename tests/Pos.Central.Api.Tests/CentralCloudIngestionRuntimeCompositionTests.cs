using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class CentralCloudIngestionRuntimeCompositionTests
{
    [Fact]
    public void Disabled_configuration_binds_and_requires_no_security_runtime()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["CentralCloudIngestion:Enabled"] = "false"
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider
            .GetRequiredService<IOptions<CentralCloudIngestionOptions>>()
            .Value;

        Assert.False(options.Enabled);
        Assert.Equal(string.Empty, options.AuthenticationScheme);
        Assert.Null(provider.GetService<IAuthorizationPolicyProvider>());
    }

    [Fact]
    public async Task Enabled_configuration_binds_scheme_to_authorization_policy()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["CentralCloudIngestion:Enabled"] = "true",
            ["CentralCloudIngestion:AuthenticationScheme"] =
                "CentralBranchCertificate"
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider
            .GetRequiredService<IOptions<CentralCloudIngestionOptions>>()
            .Value;

        Assert.True(options.Enabled);
        Assert.Equal("CentralBranchCertificate", options.AuthenticationScheme);
        var policy = Assert.IsType<AuthorizationPolicy>(
            await provider
                .GetRequiredService<IAuthorizationPolicyProvider>()
                .GetPolicyAsync(
                    CentralCloudIngestionAuthorizationDefaults.PolicyName));
        Assert.Equal(["CentralBranchCertificate"], policy.AuthenticationSchemes);
    }

    [Fact]
    public void Enabled_configuration_fails_startup_without_canonical_scheme()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["CentralCloudIngestion:Enabled"] = "true",
            ["CentralCloudIngestion:AuthenticationScheme"] = " "
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "canonical ASCII identifier",
                StringComparison.Ordinal));
        Assert.Null(provider.GetService<IAuthorizationPolicyProvider>());
    }

    [Fact]
    public async Task Repeated_registration_does_not_duplicate_runtime_services()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["CentralCloudIngestion:Enabled"] = "true",
            ["CentralCloudIngestion:AuthenticationScheme"] =
                "CentralBranchCertificate"
        });
        var services = new ServiceCollection();
        services.AddCentralCloudIngestionRuntimeConfiguration(configuration);
        services.AddCentralCloudIngestionRuntimeConfiguration(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Single(
            provider.GetServices<IValidateOptions<CentralCloudIngestionOptions>>(),
            validator => validator is CentralCloudIngestionOptionsValidator);
        Assert.Single(
            provider.GetServices<IAuthorizationHandler>(),
            handler => handler is CentralCloudIngestionAuthorizationHandler);
        var policy = Assert.IsType<AuthorizationPolicy>(
            await provider
                .GetRequiredService<IAuthorizationPolicyProvider>()
                .GetPolicyAsync(
                    CentralCloudIngestionAuthorizationDefaults.PolicyName));
        Assert.Equal(["CentralBranchCertificate"], policy.AuthenticationSchemes);
    }

    [Fact]
    public void Returns_the_supplied_service_collection()
    {
        var services = new ServiceCollection();

        var returned = services.AddCentralCloudIngestionRuntimeConfiguration(
            Configuration(new Dictionary<string, string?>()));

        Assert.Same(services, returned);
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var configuration = Configuration(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            CentralCloudIngestionRuntimeServiceCollectionExtensions
                .AddCentralCloudIngestionRuntimeConfiguration(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCentralCloudIngestionRuntimeConfiguration(null!));
    }

    private static ServiceProvider Provider(
        IReadOnlyDictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddCentralCloudIngestionRuntimeConfiguration(
            Configuration(values));
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
