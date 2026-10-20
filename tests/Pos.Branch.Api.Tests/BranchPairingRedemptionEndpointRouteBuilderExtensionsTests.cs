using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingRedemptionEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void Maps_one_named_anonymous_post_route_excluded_from_openapi()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(
            new BranchPairingRedemptionService(
                new RejectingStore(),
                TimeProvider.System));
        var app = builder.Build();

        app.MapBranchPairingRedemptionEndpoint();

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)));
        Assert.Equal(
            BranchPairingRedemptionEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
        Assert.Equal(
            BranchPairingRedemptionEndpointRouteBuilderExtensions.EndpointName,
            route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            HttpMethods.Post,
            Assert.IsAssignableFrom<IHttpMethodMetadata>(
                route.Metadata.GetMetadata<IHttpMethodMetadata>()).HttpMethods);
        Assert.NotNull(route.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal(
            BranchPairingRedemptionRateLimitPolicy.PolicyName,
            route.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal(
            BranchPairingRedemptionEndpointRouteBuilderExtensions.MaxRequestBodySize,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.True(
            route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                ?.ExcludeFromDescription);
        Assert.Null(route.Metadata.GetMetadata<IAuthorizeData>());
    }

    [Fact]
    public void Missing_route_builder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchPairingRedemptionEndpointRouteBuilderExtensions
                .MapBranchPairingRedemptionEndpoint(null!));
    }

    [Fact]
    public void Disabled_pairing_does_not_map_redemption_route()
    {
        var app = WebApplication.CreateBuilder().Build();

        app.MapBranchPairingRedemptionEndpointIfEnabled(Configuration(enabled: false));

        Assert.Empty(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
    }

    [Fact]
    public void Enabled_pairing_maps_one_redemption_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(
            new BranchPairingRedemptionService(
                new RejectingStore(),
                TimeProvider.System));
        var app = builder.Build();

        app.MapBranchPairingRedemptionEndpointIfEnabled(Configuration(enabled: true));

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)));
        Assert.Equal(
            BranchPairingRedemptionEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
    }

    [Fact]
    public void Conditional_mapping_rejects_null_arguments()
    {
        var configuration = Configuration(enabled: false);
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            BranchPairingRedemptionEndpointRouteBuilderExtensions
                .MapBranchPairingRedemptionEndpointIfEnabled(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            app.MapBranchPairingRedemptionEndpointIfEnabled(null!));
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchPairing:Enabled"] = enabled.ToString()
            })
            .Build();

    private sealed class RejectingStore : IBranchPairingRedemptionStore
    {
        public Task<bool> TryRedeemAsync(
            BranchPairingRedemptionRegistration registration,
            ReadOnlyMemory<byte> presentedPairingSecret,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
