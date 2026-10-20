using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
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

    private sealed class RecordingStore : IBranchPairingTokenIssuanceStore
    {
        public Task ReplaceActiveForCreatorAsync(
            BranchPairingTokenRegistration registration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
