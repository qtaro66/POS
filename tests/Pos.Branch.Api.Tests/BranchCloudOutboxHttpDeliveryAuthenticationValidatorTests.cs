using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudOutboxHttpDeliveryAuthenticationValidatorTests
{
    [Fact]
    public void Disabled_delivery_requires_no_authenticated_handler()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var validator = Validator(provider);

        var result = validator.Validate(
            null,
            new BranchCloudOutboxDeliveryOptions { Enabled = false });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Enabled_delivery_fails_without_authenticated_handler()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var validator = Validator(provider);

        var result = validator.Validate(
            null,
            new BranchCloudOutboxDeliveryOptions { Enabled = true });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                nameof(IBranchCloudDeliveryAuthenticatedHttpHandlerFactory),
                StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_delivery_accepts_registered_authenticated_handler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<
            IBranchCloudDeliveryAuthenticatedHttpHandlerFactory,
            TestHandlerFactory>();
        using var provider = services.BuildServiceProvider();
        var validator = Validator(provider);

        var result = validator.Validate(
            null,
            new BranchCloudOutboxDeliveryOptions { Enabled = true });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudOutboxHttpDeliveryAuthenticationValidator(null!));
        using var provider = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<ArgumentNullException>(() =>
            Validator(provider).Validate(null, null!));
    }

    private static BranchCloudOutboxHttpDeliveryAuthenticationValidator Validator(
        IServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceProviderIsService>());

    private sealed class TestHandlerFactory :
        IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
    {
        public HttpMessageHandler CreateHandler() => new HttpClientHandler();
    }
}
