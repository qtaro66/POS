using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class
    CentralCloudIngestionReadinessEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public void Maps_one_named_anonymous_get_route()
    {
        var app = ApplicationWithHealthChecks();

        app.MapCentralCloudIngestionReadinessEndpoint();

        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
                .RoutePattern,
            route.RoutePattern.RawText);
        Assert.Equal(
            CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
                .EndpointName,
            route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            HttpMethods.Get,
            Assert.IsAssignableFrom<IHttpMethodMetadata>(
                route.Metadata.GetMetadata<IHttpMethodMetadata>()).HttpMethods);
        Assert.NotNull(route.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Empty(route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.True(
            route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                ?.ExcludeFromDescription);
    }

    [Fact]
    public void Endpoint_contract_uses_stable_readiness_path()
    {
        Assert.Equal(
            "/health/ready",
            CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
                .RoutePattern);
        Assert.Equal(
            "CentralCloudIngestionReadiness",
            CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
                .EndpointName);
    }

    [Fact]
    public void Disabled_ingestion_maps_no_route()
    {
        var app = ApplicationWithHealthChecks();

        var returned = app
            .MapCentralCloudIngestionReadinessEndpointIfEnabled(
                Configuration(enabled: false));

        Assert.Same(app, returned);
        Assert.Empty(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact]
    public void Enabled_ingestion_maps_the_route()
    {
        var app = ApplicationWithHealthChecks();

        var returned = app
            .MapCentralCloudIngestionReadinessEndpointIfEnabled(
                Configuration(enabled: true));

        Assert.Same(app, returned);
        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        Assert.Equal(
            CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
                .RoutePattern,
            route.RoutePattern.RawText);
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var configuration = Configuration(enabled: false);
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
                .MapCentralCloudIngestionReadinessEndpoint(null!));
        Assert.Throws<ArgumentNullException>(() =>
            CentralCloudIngestionReadinessEndpointRouteBuilderExtensions
                .MapCentralCloudIngestionReadinessEndpointIfEnabled(
                    null!,
                    configuration));
        Assert.Throws<ArgumentNullException>(() =>
            app.MapCentralCloudIngestionReadinessEndpointIfEnabled(null!));
    }

    [Theory]
    [InlineData(true, StatusCodes.Status200OK, "Healthy")]
    [InlineData(false, StatusCodes.Status503ServiceUnavailable, "Unhealthy")]
    public async Task Response_contains_only_aggregate_status(
        bool ready,
        int expectedStatusCode,
        string expectedBody)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPostgresCloudIngestionReadinessProbe>(
            new StubProbe(ready));
        builder.Services
            .AddHealthChecks()
            .AddCheck<CentralCloudIngestionReadinessHealthCheck>(
                CentralCloudIngestionReadinessHealthCheck.Name,
                tags: [CentralCloudIngestionReadinessHealthCheck.Tag]);
        await using var app = builder.Build();
        app.MapCentralCloudIngestionReadinessEndpoint();
        var route = Assert.IsType<RouteEndpoint>(Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(
                source => source.Endpoints)));
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() }
        };

        await route.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.Equal(expectedBody, body);
        Assert.Equal("text/plain; charset=utf-8", context.Response.ContentType);
        Assert.DoesNotContain("event_id", body, StringComparison.Ordinal);
        Assert.DoesNotContain("probe_failed", body, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CentralCloudIngestion:Enabled"] = enabled.ToString()
            })
            .Build();

    private static WebApplication ApplicationWithHealthChecks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHealthChecks();
        return builder.Build();
    }

    private sealed class StubProbe(bool ready) :
        IPostgresCloudIngestionReadinessProbe
    {
        public int ExpectedSchemaVersion => 1;

        public ValueTask<PostgresCloudIngestionReadinessResult> CheckAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new PostgresCloudIngestionReadinessResult(
                    IsReady: ready,
                    ExpectedSchemaVersion,
                    ActualSchemaVersion: 1,
                    MigrationTablePresent: true,
                    BranchCloudEventsTablePresent: true,
                    MissingBranchCloudEventColumns: ready ? [] : ["event_id"],
                    ErrorCode: ready
                        ? null
                        : PostgresCloudIngestionReadinessErrorCodes.ProbeFailed));
    }
}
