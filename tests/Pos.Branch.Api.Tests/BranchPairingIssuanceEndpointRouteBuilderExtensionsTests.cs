using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingIssuanceEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void Maps_one_named_authenticated_bodyless_post_route_excluded_from_openapi()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Options.Create(new BranchPairingOptions
        {
            ServiceOrigin = "https://192.0.2.10:8443"
        }));
        builder.Services.AddSingleton(
            new BranchPairingTokenIssuanceService(
                new RecordingStore(),
                TimeProvider.System));
        var app = builder.Build();

        app.MapBranchPairingIssuanceEndpoint();

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)));
        Assert.Equal(
            BranchPairingIssuanceEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
        Assert.Equal(
            BranchPairingIssuanceEndpointRouteBuilderExtensions.EndpointName,
            route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            HttpMethods.Post,
            Assert.IsAssignableFrom<IHttpMethodMetadata>(
                route.Metadata.GetMetadata<IHttpMethodMetadata>()).HttpMethods);
        Assert.Equal(
            BranchPairingIssuanceEndpointRouteBuilderExtensions.MaxRequestBodySize,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.True(
            route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                ?.ExcludeFromDescription);
        var authorization = Assert.Single(route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Equal(
            BranchPairingIssuanceEndpointRouteBuilderExtensions.AuthorizationPolicyName,
            authorization.Policy);
        Assert.Null(route.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public void Missing_route_builder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchPairingIssuanceEndpointRouteBuilderExtensions
                .MapBranchPairingIssuanceEndpoint(null!));
    }

    [Fact]
    public void Disabled_pairing_maps_no_issuance_route()
    {
        var app = WebApplication.CreateBuilder().Build();

        var returned = app.MapBranchPairingIssuanceEndpointIfEnabled(
            Configuration(enabled: false));

        Assert.Same(app, returned);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact]
    public void Enabled_pairing_maps_the_issuance_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Options.Create(new BranchPairingOptions
        {
            ServiceOrigin = "https://192.0.2.10:8443"
        }));
        builder.Services.AddSingleton(
            new BranchPairingTokenIssuanceService(
                new RecordingStore(),
                TimeProvider.System));
        var app = builder.Build();

        var returned = app.MapBranchPairingIssuanceEndpointIfEnabled(
            Configuration(enabled: true));

        Assert.Same(app, returned);
        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            BranchPairingIssuanceEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
    }

    [Fact]
    public void Conditional_mapping_rejects_null_arguments()
    {
        var configuration = Configuration(enabled: false);
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            BranchPairingIssuanceEndpointRouteBuilderExtensions
                .MapBranchPairingIssuanceEndpointIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            app.MapBranchPairingIssuanceEndpointIfEnabled(null!));
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchPairing:Enabled"] = enabled.ToString()
            })
            .Build();

    private sealed class RecordingStore : IBranchPairingTokenIssuanceStore
    {
        public Task ReplaceActiveForCreatorAsync(
            BranchPairingTokenRegistration registration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
