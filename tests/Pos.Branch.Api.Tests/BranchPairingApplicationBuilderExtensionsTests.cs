using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task Disabled_pairing_adds_no_rate_limiter_middleware()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;

        application.UseBranchPairingRateLimiting(Configuration(enabled: false));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });

        await application.Build()(new DefaultHttpContext
        {
            RequestServices = provider
        });

        Assert.True(reachedTerminal);
    }

    [Fact]
    public async Task Enabled_pairing_composes_registered_rate_limiter_middleware()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                BranchPairingRedemptionRateLimitPolicy.PolicyName,
                new BranchPairingRedemptionRateLimitPolicy());
        });
        using var provider = services.BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var terminalCount = 0;

        application.UseBranchPairingRateLimiting(Configuration(enabled: true));
        application.Run(_ =>
        {
            terminalCount++;
            return Task.CompletedTask;
        });

        var pipeline = application.Build();
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new EnableRateLimitingAttribute(
                    BranchPairingRedemptionRateLimitPolicy.PolicyName)),
            "pairing redemption test endpoint");
        DefaultHttpContext? rejected = null;
        for (var attempt = 0;
             attempt <= BranchPairingRedemptionRateLimitPolicy.PermitLimit;
             attempt++)
        {
            var context = new DefaultHttpContext
            {
                RequestServices = provider
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
            context.SetEndpoint(endpoint);
            await pipeline(context);
            rejected = context;
        }

        Assert.Equal(
            BranchPairingRedemptionRateLimitPolicy.PermitLimit,
            terminalCount);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected!.Response.StatusCode);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var configuration = Configuration(enabled: false);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);

        Assert.Throws<ArgumentNullException>(() =>
            BranchPairingApplicationBuilderExtensions
                .UseBranchPairingRateLimiting(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            application.UseBranchPairingRateLimiting(null!));
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchPairing:Enabled"] = enabled.ToString()
            })
            .Build();
}
