using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
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

    private sealed class RejectingStore : IBranchPairingRedemptionStore
    {
        public Task<bool> TryRedeemAsync(
            BranchPairingRedemptionRegistration registration,
            ReadOnlyMemory<byte> presentedPairingSecret,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
