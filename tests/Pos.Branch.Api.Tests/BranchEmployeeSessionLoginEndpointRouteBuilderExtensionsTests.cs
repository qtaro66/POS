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

public sealed class BranchEmployeeSessionLoginEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void Maps_one_named_device_authenticated_post_route_excluded_from_openapi()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(LoginService());
        var app = builder.Build();

        app.MapBranchEmployeeSessionLoginEndpoint();

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            BranchEmployeeSessionLoginEndpointRouteBuilderExtensions.RoutePattern,
            route.RoutePattern.RawText);
        Assert.Equal(
            BranchEmployeeSessionLoginEndpointRouteBuilderExtensions.EndpointName,
            route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            HttpMethods.Post,
            Assert.IsAssignableFrom<IHttpMethodMetadata>(
                route.Metadata.GetMetadata<IHttpMethodMetadata>()).HttpMethods);
        var authorization = Assert.Single(route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Equal(
            BranchDeviceAuthenticationDefaults.AuthorizationPolicyName,
            authorization.Policy);
        Assert.Null(route.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.True(
            route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                ?.ExcludeFromDescription);
        Assert.Null(route.Metadata.GetMetadata<EnableRateLimitingAttribute>());
        Assert.Equal(
            BranchEmployeeSessionLoginEndpointRouteBuilderExtensions.MaxRequestBodySize,
            route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
    }

    [Fact]
    public void Missing_route_builder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchEmployeeSessionLoginEndpointRouteBuilderExtensions
                .MapBranchEmployeeSessionLoginEndpoint(null!));
    }

    private static BranchEmployeeSessionLoginService LoginService() =>
        new(
            new BranchEmployeePinAuthenticationService(new RejectingPinValidator()),
            new BranchEmployeeSessionIssuanceService(
                new NoopSessionStore(),
                new FixedBranchEmployeeSessionLifetimePolicy(TimeSpan.FromHours(8)),
                TimeProvider.System));

    private sealed class RejectingPinValidator : IBranchEmployeePinValidator
    {
        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(
            string branchId,
            ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VerifiedBranchEmployee?>(null);
    }

    private sealed class NoopSessionStore : IBranchEmployeeSessionIssuanceStore
    {
        public Task CreateAsync(
            BranchEmployeeSessionRegistration registration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
