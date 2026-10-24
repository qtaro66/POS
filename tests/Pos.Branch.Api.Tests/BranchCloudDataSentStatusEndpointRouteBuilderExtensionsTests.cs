using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudDataSentStatusEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void Maps_one_named_complete_identity_get_route_without_body()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IBranchCloudDataSentStatusQuery>(
            new NeverSentQuery());
        var app = builder.Build();

        app.MapBranchCloudDataSentStatusEndpoint();

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            BranchCloudDataSentStatusEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
        Assert.Equal(
            BranchCloudDataSentStatusEndpointRouteBuilderExtensions.EndpointName,
            route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            HttpMethods.Get,
            Assert.IsAssignableFrom<IHttpMethodMetadata>(
                route.Metadata.GetMetadata<IHttpMethodMetadata>()).HttpMethods);
        var authorization = Assert.Single(
            route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Equal(
            BranchPosAuthorizationDefaults.CompleteIdentityPolicyName,
            authorization.Policy);
        Assert.Null(route.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.True(
            route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                ?.ExcludeFromDescription);
        Assert.Equal(
            BranchCloudDataSentStatusEndpointRouteBuilderExtensions
                .MaxRequestBodySize,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()
                ?.MaxRequestBodySize);
    }

    [Fact]
    public void Missing_route_builder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudDataSentStatusEndpointRouteBuilderExtensions
                .MapBranchCloudDataSentStatusEndpoint(null!));
    }

    [Fact]
    public void Disabled_status_maps_no_route()
    {
        var app = WebApplication.CreateBuilder().Build();

        var returned = app.MapBranchCloudDataSentStatusEndpointIfEnabled(
            Configuration(enabled: false));

        Assert.Same(app, returned);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact]
    public void Enabled_status_maps_the_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IBranchCloudDataSentStatusQuery>(
            new NeverSentQuery());
        var app = builder.Build();

        var returned = app.MapBranchCloudDataSentStatusEndpointIfEnabled(
            Configuration(enabled: true));

        Assert.Same(app, returned);
        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            BranchCloudDataSentStatusEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
    }

    [Fact]
    public void Conditional_mapping_rejects_null_arguments()
    {
        var configuration = Configuration(enabled: false);
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudDataSentStatusEndpointRouteBuilderExtensions
                .MapBranchCloudDataSentStatusEndpointIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            app.MapBranchCloudDataSentStatusEndpointIfEnabled(null!));
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchCloudDataSentStatus:Enabled"] = enabled.ToString()
            })
            .Build();

    private sealed class NeverSentQuery : IBranchCloudDataSentStatusQuery
    {
        public Task<BranchCloudDataSentStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BranchCloudDataSentStatus?>(null);
    }
}
