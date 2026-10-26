using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class CentralCloudIngestionApplicationBuilderExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runtime_security_is_conditional_and_keeps_pipeline_available(
        bool enabled)
    {
        var services = new ServiceCollection();
        if (enabled)
        {
            services.AddLogging();
            services.AddRouting();
            services.AddAuthentication();
            services.AddAuthorization();
        }

        using var provider = services.BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;

        var returned = application.UseCentralCloudIngestionSecurityIfEnabled(
            Configuration(enabled));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });

        await application.Build()(new DefaultHttpContext
        {
            RequestServices = provider
        });

        Assert.Same(application, returned);
        Assert.True(reachedTerminal);
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var configuration = Configuration(enabled: false);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);

        Assert.Throws<ArgumentNullException>(() =>
            CentralCloudIngestionApplicationBuilderExtensions
                .UseCentralCloudIngestionSecurityIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            application.UseCentralCloudIngestionSecurityIfEnabled(null!));
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CentralCloudIngestion:Enabled"] = enabled.ToString()
            })
            .Build();
}
