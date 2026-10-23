using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void Maps_one_named_complete_identity_get_route_without_body()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IBranchEmployeeDataSyncStatusQuery>(
            new NeverSynchronizedQuery());
        var app = builder.Build();

        app.MapBranchEmployeeDataSyncStatusEndpoint();

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
        Assert.Equal(
            BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensions.EndpointName,
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
            BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensions
                .MaxRequestBodySize,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()
                ?.MaxRequestBodySize);
    }

    [Fact]
    public void Missing_route_builder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensions
                .MapBranchEmployeeDataSyncStatusEndpoint(null!));
    }

    [Fact]
    public void Disabled_sessions_map_no_sync_status_route()
    {
        var app = WebApplication.CreateBuilder().Build();

        var returned = app.MapBranchEmployeeDataSyncStatusEndpointIfEnabled(
            Configuration(enabled: false));

        Assert.Same(app, returned);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact]
    public void Enabled_sessions_map_the_sync_status_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IBranchEmployeeDataSyncStatusQuery>(
            new NeverSynchronizedQuery());
        var app = builder.Build();

        var returned = app.MapBranchEmployeeDataSyncStatusEndpointIfEnabled(
            Configuration(enabled: true));

        Assert.Same(app, returned);
        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
    }

    [Fact]
    public void Conditional_mapping_rejects_null_arguments()
    {
        var configuration = Configuration(enabled: false);
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            BranchEmployeeDataSyncStatusEndpointRouteBuilderExtensions
                .MapBranchEmployeeDataSyncStatusEndpointIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            app.MapBranchEmployeeDataSyncStatusEndpointIfEnabled(null!));
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchEmployeeSessions:Enabled"] = enabled.ToString()
            })
            .Build();

    private sealed class NeverSynchronizedQuery :
        IBranchEmployeeDataSyncStatusQuery
    {
        public Task<BranchEmployeeDataSyncStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BranchEmployeeDataSyncStatus?>(null);
    }
}
