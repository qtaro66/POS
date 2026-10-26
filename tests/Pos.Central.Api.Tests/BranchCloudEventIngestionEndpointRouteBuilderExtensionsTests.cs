using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class BranchCloudEventIngestionEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void Maps_one_named_authorized_post_route_with_bounded_body()
    {
        var app = WebApplication.CreateBuilder().Build();

        app.MapBranchCloudEventIngestionEndpoint();

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            BranchCloudEventIngestionEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
        Assert.Equal(
            BranchCloudEventIngestionEndpointRouteBuilderExtensions.EndpointName,
            route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            HttpMethods.Post,
            Assert.IsAssignableFrom<IHttpMethodMetadata>(
                route.Metadata.GetMetadata<IHttpMethodMetadata>()).HttpMethods);

        var authorization = Assert.Single(
            route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Equal(
            BranchCloudEventIngestionEndpointRouteBuilderExtensions
                .AuthorizationPolicyName,
            authorization.Policy);
        Assert.Equal(
            CentralCloudIngestionAuthorizationDefaults.PolicyName,
            authorization.Policy);
        Assert.Null(route.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal(
            BranchCloudEventIngestionEndpointRouteBuilderExtensions
                .MaxRequestBodySize,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()
                ?.MaxRequestBodySize);
        Assert.True(
            route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                ?.ExcludeFromDescription);
    }

    [Fact]
    public void Endpoint_contract_uses_the_configured_branch_delivery_path()
    {
        Assert.Equal(
            "/api/v1/branch-events",
            BranchCloudEventIngestionEndpointRouteBuilderExtensions.RoutePattern);
        Assert.Equal(
            16 * 1024,
            BranchCloudEventIngestionEndpointRouteBuilderExtensions
                .MaxRequestBodySize);
    }

    [Fact]
    public void Missing_route_builder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudEventIngestionEndpointRouteBuilderExtensions
                .MapBranchCloudEventIngestionEndpoint(null!));
    }
}
