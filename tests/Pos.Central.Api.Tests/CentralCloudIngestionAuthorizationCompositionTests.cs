using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class CentralCloudIngestionAuthorizationCompositionTests
{
    [Fact]
    public async Task Registers_named_policy_and_strict_handler()
    {
        using var provider = Provider(registerTwice: false);

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = Assert.IsType<AuthorizationPolicy>(
            await policyProvider.GetPolicyAsync(
                CentralCloudIngestionAuthorizationDefaults.PolicyName));

        Assert.Empty(policy.AuthenticationSchemes);
        Assert.Contains(
            policy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(
            policy.Requirements,
            requirement =>
                requirement is CentralCloudIngestionAuthorizationRequirement);
        Assert.Single(
            provider.GetServices<IAuthorizationHandler>(),
            handler => handler is CentralCloudIngestionAuthorizationHandler);
    }

    [Fact]
    public async Task Explicit_scheme_is_bound_to_the_named_policy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentralCloudIngestionAuthorization(
            "CentralBranchCertificate");
        using var provider = services.BuildServiceProvider();

        var policy = Assert.IsType<AuthorizationPolicy>(
            await provider
                .GetRequiredService<IAuthorizationPolicyProvider>()
                .GetPolicyAsync(
                    CentralCloudIngestionAuthorizationDefaults.PolicyName));

        Assert.Equal(["CentralBranchCertificate"], policy.AuthenticationSchemes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("central branch")]
    [InlineData("-central-branch")]
    public void Noncanonical_explicit_scheme_is_rejected(string scheme)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddCentralCloudIngestionAuthorization(scheme));

        Assert.Equal("authenticationScheme", exception.ParamName);
        Assert.Empty(services);
    }

    [Fact]
    public void Repeated_registration_does_not_duplicate_the_custom_handler()
    {
        using var provider = Provider(registerTwice: true);

        Assert.Single(
            provider.GetServices<IAuthorizationHandler>(),
            handler => handler is CentralCloudIngestionAuthorizationHandler);
    }

    [Fact]
    public void Returns_the_supplied_service_collection()
    {
        var services = new ServiceCollection();

        var returned = services.AddCentralCloudIngestionAuthorization();

        Assert.Same(services, returned);
    }

    [Fact]
    public void Missing_service_collection_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CentralCloudIngestionAuthorizationServiceCollectionExtensions
                .AddCentralCloudIngestionAuthorization(null!));
    }

    private static ServiceProvider Provider(bool registerTwice)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentralCloudIngestionAuthorization();
        if (registerTwice)
        {
            services.AddCentralCloudIngestionAuthorization();
        }

        return services.BuildServiceProvider();
    }
}
