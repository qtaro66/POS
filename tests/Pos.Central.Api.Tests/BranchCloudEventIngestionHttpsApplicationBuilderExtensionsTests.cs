using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class BranchCloudEventIngestionHttpsApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task Http_ingestion_is_rejected_before_body_or_downstream_access()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;
        application.UseBranchCloudEventIngestionHttpsOnly(
            Configuration(enabled: true));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });
        var body = new MemoryStream([1, 2, 3, 4]);
        var context = Context(
            provider,
            "http",
            BranchCloudEventIngestionEndpointRouteBuilderExtensions.RoutePattern);
        context.Request.Body = body;

        await application.Build()(context);

        Assert.False(reachedTerminal);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, body.Position);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.Location));
    }

    [Theory]
    [InlineData("https", "/api/v1/branch-events")]
    [InlineData("http", "/health")]
    [InlineData("http", "/api/v1/branch-event")]
    [InlineData("http", "/api/v1/branch-events-other")]
    public async Task Https_and_unrelated_paths_continue_downstream(
        string scheme,
        string path)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;
        application.UseBranchCloudEventIngestionHttpsOnly(
            Configuration(enabled: true));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });

        await application.Build()(Context(provider, scheme, path));

        Assert.True(reachedTerminal);
    }

    [Fact]
    public async Task Http_ingestion_subpath_is_rejected()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;
        application.UseBranchCloudEventIngestionHttpsOnly(
            Configuration(enabled: true));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });
        var path =
            $"{BranchCloudEventIngestionEndpointRouteBuilderExtensions.RoutePattern}/details";

        var context = Context(provider, "http", path);
        await application.Build()(context);

        Assert.False(reachedTerminal);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Disabled_ingestion_adds_no_https_guard()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;
        application.UseBranchCloudEventIngestionHttpsOnly(
            Configuration(enabled: false));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });

        await application.Build()(Context(
            provider,
            "http",
            BranchCloudEventIngestionEndpointRouteBuilderExtensions.RoutePattern));

        Assert.True(reachedTerminal);
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var configuration = Configuration(enabled: false);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);

        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudEventIngestionHttpsApplicationBuilderExtensions
                .UseBranchCloudEventIngestionHttpsOnly(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            application.UseBranchCloudEventIngestionHttpsOnly(null!));
    }

    private static DefaultHttpContext Context(
        IServiceProvider provider,
        string scheme,
        string path)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Request.Scheme = scheme;
        context.Request.Path = path;
        return context;
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CentralCloudIngestion:Enabled"] = enabled.ToString()
            })
            .Build();
}
