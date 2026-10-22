using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionHttpsApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task Enabled_sessions_reject_http_before_body_or_downstream_access()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;
        application.UseBranchEmployeeSessionHttpsOnly(Configuration(enabled: true));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });
        var body = new MemoryStream([1, 2, 3, 4]);
        var context = Context(provider, "http", "/api/v1/employee-sessions");
        context.Request.Body = body;

        await application.Build()(context);

        Assert.False(reachedTerminal);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, body.Position);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.Location));
    }

    [Theory]
    [InlineData("https", "/api/v1/employee-sessions")]
    [InlineData("http", "/health")]
    [InlineData("http", "/api/v1/employee-session")]
    public async Task Enabled_sessions_allow_https_and_unrelated_paths(
        string scheme,
        string path)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;
        application.UseBranchEmployeeSessionHttpsOnly(Configuration(enabled: true));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });

        await application.Build()(Context(provider, scheme, path));

        Assert.True(reachedTerminal);
    }

    [Fact]
    public async Task Disabled_sessions_add_no_https_guard()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;
        application.UseBranchEmployeeSessionHttpsOnly(Configuration(enabled: false));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });

        await application.Build()(Context(
            provider,
            "http",
            "/api/v1/employee-sessions"));

        Assert.True(reachedTerminal);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var configuration = Configuration(enabled: false);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);

        Assert.Throws<ArgumentNullException>(() =>
            BranchEmployeeSessionHttpsApplicationBuilderExtensions
                .UseBranchEmployeeSessionHttpsOnly(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            application.UseBranchEmployeeSessionHttpsOnly(null!));
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
                ["BranchEmployeeSessions:Enabled"] = enabled.ToString()
            })
            .Build();
}
