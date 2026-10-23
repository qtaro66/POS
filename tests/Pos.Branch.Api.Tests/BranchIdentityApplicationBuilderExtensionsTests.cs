using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchIdentityApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task Disabled_identity_adds_no_authentication_middleware()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;

        application.UseBranchIdentity(Configuration(false, false));
        application.Run(context =>
        {
            reachedTerminal = true;
            Assert.False(context.User.Identity?.IsAuthenticated);
            return Task.CompletedTask;
        });

        await application.Build()(new DefaultHttpContext
        {
            RequestServices = provider
        });

        Assert.True(reachedTerminal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Enabled_identity_authenticates_request(
        bool deviceAuthenticationEnabled,
        bool employeeSessionsEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;

        application.UseBranchIdentity(Configuration(
            deviceAuthenticationEnabled,
            employeeSessionsEnabled));
        application.Run(context =>
        {
            reachedTerminal = true;
            Assert.True(context.User.Identity?.IsAuthenticated);
            return Task.CompletedTask;
        });

        await application.Build()(new DefaultHttpContext
        {
            RequestServices = provider
        });

        Assert.True(reachedTerminal);
    }

    [Fact]
    public async Task Enabled_identity_enforces_endpoint_authorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });
        services.AddAuthorization(options =>
            options.AddPolicy("RequiresPermission", policy =>
                policy.RequireClaim("permission", "granted")));
        using var provider = services.BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var reachedTerminal = false;

        application.UseBranchIdentity(Configuration(true, false));
        application.Run(_ =>
        {
            reachedTerminal = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new AuthorizeAttribute("RequiresPermission")),
            "protected test endpoint"));

        await application.Build()(context);

        Assert.False(reachedTerminal);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var configuration = Configuration(false, false);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new ApplicationBuilder(provider);

        Assert.Throws<ArgumentNullException>(() =>
            BranchIdentityApplicationBuilderExtensions
                .UseBranchIdentity(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            application.UseBranchIdentity(null!));
    }

    private static IConfiguration Configuration(
        bool deviceAuthenticationEnabled,
        bool employeeSessionsEnabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchDeviceAuthentication:Enabled"] =
                    deviceAuthenticationEnabled.ToString(),
                ["BranchEmployeeSessions:Enabled"] =
                    employeeSessionsEnabled.ToString()
            })
            .Build();

    private sealed class TestAuthenticationHandler :
        AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "employee-1") },
                SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
