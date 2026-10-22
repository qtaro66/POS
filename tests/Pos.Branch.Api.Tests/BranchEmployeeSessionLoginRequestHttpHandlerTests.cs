using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionLoginRequestHttpHandlerTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 22, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Supported_request_delegates_exact_pin_for_authenticated_device()
    {
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore();
        var response = Response();

        var result = await BranchEmployeeSessionLoginRequestHttpHandler.HandleAsync(
            DevicePrincipal(),
            new BranchEmployeeSessionLoginRequest(1, " 12 34 "),
            Service(pinValidator, store),
            response,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchEmployeeSessionLoginResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(" 12 34 ", pinValidator.PresentedPin);
        Assert.NotNull(store.Registration);
        AssertSecretResponseHeaders(response);
    }

    [Theory]
    [InlineData(0, "1234")]
    [InlineData(2, "1234")]
    [InlineData(1, null)]
    [InlineData(1, "")]
    [InlineData(1, " ")]
    public async Task Unsupported_version_or_empty_pin_returns_bodyless_400(
        int schemaVersion,
        string? pin)
    {
        var pinValidator = new RecordingPinValidator(result: null);
        var store = new RecordingSessionStore();
        var response = Response();

        var result = await BranchEmployeeSessionLoginRequestHttpHandler.HandleAsync(
            DevicePrincipal(),
            new BranchEmployeeSessionLoginRequest(schemaVersion, pin!),
            Service(pinValidator, store),
            response,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal(0, pinValidator.CallCount);
        Assert.Null(store.Registration);
        AssertSecretResponseHeaders(response);
    }

    [Fact]
    public async Task Valid_request_with_invalid_principal_returns_bodyless_401()
    {
        var pinValidator = new RecordingPinValidator(result: null);
        var response = Response();

        var result = await BranchEmployeeSessionLoginRequestHttpHandler.HandleAsync(
            principal: null,
            new BranchEmployeeSessionLoginRequest(1, "1234"),
            Service(pinValidator, new RecordingSessionStore()),
            response,
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Equal(0, pinValidator.CallCount);
        AssertSecretResponseHeaders(response);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_pin_validation_and_session_storage()
    {
        using var source = new CancellationTokenSource();
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore();

        await BranchEmployeeSessionLoginRequestHttpHandler.HandleAsync(
            DevicePrincipal(),
            new BranchEmployeeSessionLoginRequest(1, "1234"),
            Service(pinValidator, store),
            Response(),
            source.Token);

        Assert.Equal(source.Token, pinValidator.CancellationToken);
        Assert.Equal(source.Token, store.CancellationToken);
    }

    private static ClaimsPrincipal DevicePrincipal() =>
        new(new ClaimsIdentity(
        [
            new Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
            new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01")
        ],
        "DeviceAuthentication"));

    private static BranchEmployeeSessionLoginService Service(
        IBranchEmployeePinValidator pinValidator,
        RecordingSessionStore store) =>
        new(
            new BranchEmployeePinAuthenticationService(pinValidator),
            new BranchEmployeeSessionIssuanceService(
                store,
                new FixedBranchEmployeeSessionLifetimePolicy(TimeSpan.FromHours(8)),
                new FixedTimeProvider(IssuedAt)));

    private static HttpResponse Response() => new DefaultHttpContext().Response;

    private static void AssertSecretResponseHeaders(HttpResponse response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl);
        Assert.Equal("no-cache", response.Headers.Pragma);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingPinValidator : IBranchEmployeePinValidator
    {
        private readonly VerifiedBranchEmployee? _result;

        public RecordingPinValidator(VerifiedBranchEmployee? result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public string? PresentedPin { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(
            string branchId,
            ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            PresentedPin = new string(pin.Span);
            CancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingSessionStore : IBranchEmployeeSessionIssuanceStore
    {
        public BranchEmployeeSessionRegistration? Registration { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task CreateAsync(
            BranchEmployeeSessionRegistration registration,
            CancellationToken cancellationToken = default)
        {
            Registration = registration;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
