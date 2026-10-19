using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchDeviceAuthenticationHandlerTests
{
    [Theory]
    [InlineData("Device")]
    [InlineData("device")]
    [InlineData("DEVICE")]
    public async Task Valid_credential_issues_only_authenticated_device_claims(
        string authorizationScheme)
    {
        await using var database = await AuthenticationDatabase.CreateAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        var validator = new RecordingValidator(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
        using var provider = Provider(database.Path, validator);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var context = Context(provider);
        context.Request.Headers.Authorization = $"{authorizationScheme} {issued.Token}";

        var result = await AuthenticateAsync(provider, context);

        Assert.True(result.Succeeded);
        var principal = Assert.IsType<System.Security.Claims.ClaimsPrincipal>(result.Principal);
        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal(
            "BRANCH-01",
            principal.FindFirst(BranchPosClaimTypes.BranchId)?.Value);
        Assert.Equal(
            "DEVICE-01",
            principal.FindFirst(BranchPosClaimTypes.DeviceId)?.Value);
        Assert.Null(principal.FindFirst(BranchPosClaimTypes.EmployeeId));
        Assert.False(AuthenticatedBranchRequestIdentityResolver.TryResolve(
            principal,
            out var requestIdentity));
        Assert.Null(requestIdentity);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public async Task Missing_authorization_header_returns_no_result()
    {
        await using var database = await AuthenticationDatabase.CreateAsync();
        var validator = new RecordingValidator(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
        using var provider = Provider(database.Path, validator);
        provider.GetRequiredService<IStartupValidator>().Validate();

        var result = await AuthenticateAsync(provider, Context(provider));

        Assert.True(result.None);
        Assert.Equal(0, validator.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer token")]
    [InlineData("Device")]
    [InlineData("Device ")]
    [InlineData("Device invalid")]
    public async Task Malformed_or_unsupported_header_fails_without_identity(
        string authorization)
    {
        await using var database = await AuthenticationDatabase.CreateAsync();
        var validator = new RecordingValidator(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
        using var provider = Provider(database.Path, validator);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var context = Context(provider);
        context.Request.Headers.Authorization = authorization;

        var result = await AuthenticateAsync(provider, context);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Equal("Invalid Branch device credential.", result.Failure.Message);
        Assert.Null(result.Principal);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public async Task Multiple_authorization_values_fail_before_validation()
    {
        await using var database = await AuthenticationDatabase.CreateAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        var validator = new RecordingValidator(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
        using var provider = Provider(database.Path, validator);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var context = Context(provider);
        context.Request.Headers.Authorization = new StringValues(
        [
            $"Device {issued.Token}",
            $"Device {issued.Token}"
        ]);

        var result = await AuthenticateAsync(provider, context);

        Assert.NotNull(result.Failure);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public async Task Inactive_credential_returns_generic_authentication_failure()
    {
        await using var database = await AuthenticationDatabase.CreateAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        var validator = new RecordingValidator(result: null);
        using var provider = Provider(database.Path, validator);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var context = Context(provider);
        context.Request.Headers.Authorization = $"Device {issued.Token}";

        var result = await AuthenticateAsync(provider, context);

        Assert.NotNull(result.Failure);
        Assert.Equal("Invalid Branch device credential.", result.Failure.Message);
        Assert.Null(result.Principal);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public async Task Validator_failure_is_not_converted_to_invalid_credential()
    {
        await using var database = await AuthenticationDatabase.CreateAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        var validator = new RecordingValidator(
            result: null,
            exception: new InvalidOperationException("Synthetic database failure."));
        using var provider = Provider(database.Path, validator);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var context = Context(provider);
        context.Request.Headers.Authorization = $"Device {issued.Token}";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AuthenticateAsync(provider, context));

        Assert.Equal("Synthetic database failure.", error.Message);
    }

    private static Task<AuthenticateResult> AuthenticateAsync(
        IServiceProvider provider,
        HttpContext context) =>
        provider.GetRequiredService<IAuthenticationService>().AuthenticateAsync(
            context,
            BranchDeviceAuthenticationDefaults.SchemeName);

    private static DefaultHttpContext Context(IServiceProvider provider) =>
        new() { RequestServices = provider };

    private static ServiceProvider Provider(
        string databasePath,
        IBranchDeviceCredentialValidator validator)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BranchDeviceAuthentication:Enabled"] = "true",
                ["BranchDeviceAuthentication:DatabasePath"] = databasePath
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(validator);
        services.AddBranchDeviceAuthentication(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingValidator : IBranchDeviceCredentialValidator
    {
        private readonly VerifiedBranchDevice? _result;
        private readonly Exception? _exception;

        public RecordingValidator(
            VerifiedBranchDevice? result,
            Exception? exception = null)
        {
            _result = result;
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public Task<VerifiedBranchDevice?> ValidateActiveAsync(
            string credentialId,
            ReadOnlyMemory<byte> credentialSecret,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _exception is null
                ? Task.FromResult(_result)
                : Task.FromException<VerifiedBranchDevice?>(_exception);
        }
    }

    private sealed class AuthenticationDatabase : IAsyncDisposable
    {
        private readonly string _directory;

        private AuthenticationDatabase(string directory)
        {
            _directory = directory;
        }

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public static async Task<AuthenticationDatabase> CreateAsync()
        {
            var database = new AuthenticationDatabase(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pos-device-auth-handler-tests",
                Guid.NewGuid().ToString("N")));
            await new SqliteTableSessionCommandStore(database.Path).InitializeAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            await ValueTask.CompletedTask;
        }
    }
}
