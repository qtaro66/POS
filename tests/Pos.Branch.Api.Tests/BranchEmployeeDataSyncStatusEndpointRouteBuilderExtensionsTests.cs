using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
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

    private sealed class NeverSynchronizedQuery :
        IBranchEmployeeDataSyncStatusQuery
    {
        public Task<BranchEmployeeDataSyncStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BranchEmployeeDataSyncStatus?>(null);
    }
}
