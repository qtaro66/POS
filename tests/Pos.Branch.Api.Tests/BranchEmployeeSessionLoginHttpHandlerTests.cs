using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionLoginHttpHandlerTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Active_employee_returns_200_session_for_trusted_device()
    {
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore();
        var response = Response();

        var result = await BranchEmployeeSessionLoginHttpHandler.HandleAsync(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"),
            "1234".AsMemory(),
            Service(pinValidator, store),
            response,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchEmployeeSessionLoginResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        AssertSecretResponseHeaders(response);
        var body = Assert.IsType<BranchEmployeeSessionLoginResponse>(ok.Value);
        Assert.Equal(1, body.SchemaVersion);
        Assert.Equal("EMPLOYEE-01", body.EmployeeId);
        Assert.Equal(IssuedAt.AddHours(8), body.ExpiresAt);
        Assert.True(BranchEmployeeSessionTokenFormat.IsCanonicalSessionId(body.SessionId));
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(
            body.SessionToken,
            out var parsedToken));
        parsedToken.Dispose();
        Assert.Equal("BRANCH-01", pinValidator.BranchId);
        var registration = Assert.IsType<BranchEmployeeSessionRegistration>(
            store.Registration);
        Assert.Equal("BRANCH-01", registration.BranchId);
        Assert.Equal("DEVICE-01", registration.DeviceId);
    }

    [Fact]
    public async Task Rejected_pin_returns_one_401_without_session_issuance()
    {
        var pinValidator = new RecordingPinValidator(result: null);
        var store = new RecordingSessionStore();
        var response = Response();

        var result = await BranchEmployeeSessionLoginHttpHandler.HandleAsync(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"),
            "wrong".AsMemory(),
            Service(pinValidator, store),
            response,
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        AssertSecretResponseHeaders(response);
        Assert.Null(store.Registration);
    }

    [Fact]
    public async Task Persistence_failure_propagates_after_secret_headers_are_applied()
    {
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore(
            new IOException("Synthetic session persistence failure."));
        var response = Response();

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            BranchEmployeeSessionLoginHttpHandler.HandleAsync(
                new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"),
                "1234".AsMemory(),
                Service(pinValidator, store),
                response,
                CancellationToken.None));

        Assert.Equal("Synthetic session persistence failure.", exception.Message);
        AssertSecretResponseHeaders(response);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_validation_and_session_storage()
    {
        using var source = new CancellationTokenSource();
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore();

        await BranchEmployeeSessionLoginHttpHandler.HandleAsync(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"),
            "1234".AsMemory(),
            Service(pinValidator, store),
            Response(),
            source.Token);

        Assert.Equal(source.Token, pinValidator.CancellationToken);
        Assert.Equal(source.Token, store.CancellationToken);
    }

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

        public string? BranchId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(
            string branchId,
            ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            CancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingSessionStore : IBranchEmployeeSessionIssuanceStore
    {
        private readonly Exception? _exception;

        public RecordingSessionStore(Exception? exception = null)
        {
            _exception = exception;
        }

        public BranchEmployeeSessionRegistration? Registration { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task CreateAsync(
            BranchEmployeeSessionRegistration registration,
            CancellationToken cancellationToken = default)
        {
            Registration = registration;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.CompletedTask
                : Task.FromException(_exception);
        }
    }
}
