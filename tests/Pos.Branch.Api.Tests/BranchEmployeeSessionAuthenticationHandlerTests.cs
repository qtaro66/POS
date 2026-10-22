using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionAuthenticationHandlerTests
{
    [Fact]
    public async Task Valid_device_and_session_issue_one_complete_identity()
    {
        var deviceCredential = BranchDeviceCredentialFormat.Generate();
        var employeeSession = BranchEmployeeSessionTokenFormat.Generate();
        var sessionValidator = new RecordingSessionValidator(
            new VerifiedBranchEmployeeSession(
                "BRANCH-01",
                "DEVICE-01",
                "EMPLOYEE-01"));
        using var provider = Provider(sessionValidator);
        var context = Context(provider, deviceCredential.Token, employeeSession.Token);

        var result = await AuthenticateAsync(provider, context);

        Assert.True(result.Succeeded);
        var principal = Assert.IsType<System.Security.Claims.ClaimsPrincipal>(
            result.Principal);
        Assert.True(AuthenticatedBranchRequestIdentityResolver.TryResolve(
            principal,
            out var identity));
        Assert.Equal("BRANCH-01", identity.BranchId);
        Assert.Equal("DEVICE-01", identity.DeviceId);
        Assert.Equal("EMPLOYEE-01", identity.EmployeeId);
        Assert.Equal(1, sessionValidator.CallCount);
        Assert.Equal(("BRANCH-01", "DEVICE-01"), sessionValidator.LastBinding);
    }

    [Fact]
    public async Task Missing_employee_session_header_returns_no_result()
    {
        var sessionValidator = new RecordingSessionValidator(result: null);
        using var provider = Provider(sessionValidator);
        var context = new DefaultHttpContext { RequestServices = provider };

        var result = await AuthenticateAsync(provider, context);

        Assert.True(result.None);
        Assert.Equal(0, sessionValidator.CallCount);
    }

    [Fact]
    public async Task Multiple_employee_session_headers_fail_before_validation()
    {
        var sessionValidator = new RecordingSessionValidator(result: null);
        using var provider = Provider(sessionValidator);
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers[
            BranchEmployeeSessionAuthenticationDefaults.HeaderName] =
            new StringValues(["session-one", "session-two"]);

        var result = await AuthenticateAsync(provider, context);

        Assert.Equal("Invalid Branch employee session.", result.Failure?.Message);
        Assert.Equal(0, sessionValidator.CallCount);
    }

    [Fact]
    public async Task Missing_device_credential_fails_before_session_validation()
    {
        var employeeSession = BranchEmployeeSessionTokenFormat.Generate();
        var sessionValidator = new RecordingSessionValidator(result: null);
        using var provider = Provider(sessionValidator);
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers[
            BranchEmployeeSessionAuthenticationDefaults.HeaderName] =
            employeeSession.Token;

        var result = await AuthenticateAsync(provider, context);

        Assert.Equal("Invalid Branch employee session.", result.Failure?.Message);
        Assert.Equal(0, sessionValidator.CallCount);
    }

    [Fact]
    public async Task Invalid_employee_session_returns_generic_failure()
    {
        var deviceCredential = BranchDeviceCredentialFormat.Generate();
        var employeeSession = BranchEmployeeSessionTokenFormat.Generate();
        var sessionValidator = new RecordingSessionValidator(result: null);
        using var provider = Provider(sessionValidator);
        var context = Context(provider, deviceCredential.Token, employeeSession.Token);

        var result = await AuthenticateAsync(provider, context);

        Assert.Equal("Invalid Branch employee session.", result.Failure?.Message);
        Assert.Null(result.Principal);
        Assert.Equal(1, sessionValidator.CallCount);
    }

    [Fact]
    public async Task Session_validator_failure_is_not_hidden()
    {
        var deviceCredential = BranchDeviceCredentialFormat.Generate();
        var employeeSession = BranchEmployeeSessionTokenFormat.Generate();
        var sessionValidator = new RecordingSessionValidator(
            result: null,
            exception: new InvalidOperationException("Synthetic database failure."));
        using var provider = Provider(sessionValidator);
        var context = Context(provider, deviceCredential.Token, employeeSession.Token);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AuthenticateAsync(provider, context));

        Assert.Equal("Synthetic database failure.", error.Message);
    }

    private static Task<AuthenticateResult> AuthenticateAsync(
        IServiceProvider provider,
        HttpContext context) =>
        provider.GetRequiredService<IAuthenticationService>().AuthenticateAsync(
            context,
            BranchEmployeeSessionAuthenticationDefaults.SchemeName);

    private static DefaultHttpContext Context(
        IServiceProvider provider,
        string deviceToken,
        string employeeSessionToken)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers.Authorization = $"Device {deviceToken}";
        context.Request.Headers[
            BranchEmployeeSessionAuthenticationDefaults.HeaderName] =
            employeeSessionToken;
        return context;
    }

    private static ServiceProvider Provider(
        IBranchEmployeeSessionValidator sessionValidator)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBranchDeviceCredentialValidator>(
            new ActiveDeviceValidator());
        services.AddSingleton<BranchDeviceTokenAuthenticationService>();
        services.AddSingleton(sessionValidator);
        services.AddSingleton<BranchEmployeeSessionAuthenticationService>();
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BranchDeviceAuthenticationHandler>(
                BranchDeviceAuthenticationDefaults.SchemeName,
                _ => { })
            .AddScheme<
                AuthenticationSchemeOptions,
                BranchEmployeeSessionAuthenticationHandler>(
                BranchEmployeeSessionAuthenticationDefaults.SchemeName,
                _ => { });
        return services.BuildServiceProvider();
    }

    private sealed class ActiveDeviceValidator : IBranchDeviceCredentialValidator
    {
        public Task<VerifiedBranchDevice?> ValidateActiveAsync(
            string credentialId,
            ReadOnlyMemory<byte> credentialSecret,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VerifiedBranchDevice?>(
                new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
    }

    private sealed class RecordingSessionValidator : IBranchEmployeeSessionValidator
    {
        private readonly VerifiedBranchEmployeeSession? _result;
        private readonly Exception? _exception;

        public RecordingSessionValidator(
            VerifiedBranchEmployeeSession? result,
            Exception? exception = null)
        {
            _result = result;
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public (string BranchId, string DeviceId)? LastBinding { get; private set; }

        public Task<VerifiedBranchEmployeeSession?> ValidateActiveAsync(
            string branchId,
            string deviceId,
            string sessionId,
            ReadOnlyMemory<byte> presentedSecret,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastBinding = (branchId, deviceId);
            return _exception is null
                ? Task.FromResult(_result)
                : Task.FromException<VerifiedBranchEmployeeSession?>(_exception);
        }
    }
}
