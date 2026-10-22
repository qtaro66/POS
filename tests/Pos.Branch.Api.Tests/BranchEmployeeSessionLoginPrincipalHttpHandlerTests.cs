using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionLoginPrincipalHttpHandlerTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 22, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Authenticated_device_claims_issue_session_for_claimed_identity()
    {
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore();
        var response = Response();

        var result = await BranchEmployeeSessionLoginPrincipalHttpHandler.HandleAsync(
            Principal(
                Claim(BranchPosClaimTypes.BranchId, " BRANCH-01 "),
                Claim(BranchPosClaimTypes.DeviceId, " DEVICE-01 ")),
            "1234".AsMemory(),
            Service(pinValidator, store),
            response,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchEmployeeSessionLoginResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        AssertSecretResponseHeaders(response);
        Assert.Equal("EMPLOYEE-01", ok.Value?.EmployeeId);
        Assert.Equal("BRANCH-01", pinValidator.BranchId);
        var registration = Assert.IsType<BranchEmployeeSessionRegistration>(
            store.Registration);
        Assert.Equal("BRANCH-01", registration.BranchId);
        Assert.Equal("DEVICE-01", registration.DeviceId);
    }

    [Fact]
    public async Task Missing_unauthenticated_or_ambiguous_device_identity_returns_same_401()
    {
        var principals = new ClaimsPrincipal?[]
        {
            null,
            new(new ClaimsIdentity(
            [
                Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
                Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01")
            ])),
            Principal(Claim(BranchPosClaimTypes.BranchId, "BRANCH-01")),
            Principal(
                Claim(BranchPosClaimTypes.BranchId, "BRANCH-01"),
                Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
                Claim(BranchPosClaimTypes.DeviceId, "DEVICE-02"))
        };

        foreach (var principal in principals)
        {
            var pinValidator = new RecordingPinValidator(result: null);
            var store = new RecordingSessionStore();
            var response = Response();

            var result = await BranchEmployeeSessionLoginPrincipalHttpHandler.HandleAsync(
                principal,
                "1234".AsMemory(),
                Service(pinValidator, store),
                response,
                CancellationToken.None);

            var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
            Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
            AssertSecretResponseHeaders(response);
            Assert.Equal(0, pinValidator.CallCount);
            Assert.Null(store.Registration);
        }
    }

    [Fact]
    public async Task Anonymous_claims_do_not_complete_authenticated_device_identity()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
            [Claim(BranchPosClaimTypes.BranchId, "BRANCH-01")],
            "DeviceAuthentication"),
            new ClaimsIdentity(
            [Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01")])
        ]);
        var pinValidator = new RecordingPinValidator(result: null);

        var result = await BranchEmployeeSessionLoginPrincipalHttpHandler.HandleAsync(
            principal,
            "1234".AsMemory(),
            Service(pinValidator, new RecordingSessionStore()),
            Response(),
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(0, pinValidator.CallCount);
    }

    [Fact]
    public async Task Claims_split_across_authenticated_identities_are_rejected()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
            [Claim(BranchPosClaimTypes.BranchId, "BRANCH-01")],
            "FirstAuthentication"),
            new ClaimsIdentity(
            [Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01")],
            "SecondAuthentication")
        ]);
        var pinValidator = new RecordingPinValidator(result: null);

        var result = await BranchEmployeeSessionLoginPrincipalHttpHandler.HandleAsync(
            principal,
            "1234".AsMemory(),
            Service(pinValidator, new RecordingSessionStore()),
            Response(),
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(0, pinValidator.CallCount);
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

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "DeviceAuthentication"));

    private static Claim Claim(string type, string value) => new(type, value);

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

        public string? BranchId { get; private set; }

        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(
            string branchId,
            ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            BranchId = branchId;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingSessionStore : IBranchEmployeeSessionIssuanceStore
    {
        public BranchEmployeeSessionRegistration? Registration { get; private set; }

        public Task CreateAsync(
            BranchEmployeeSessionRegistration registration,
            CancellationToken cancellationToken = default)
        {
            Registration = registration;
            return Task.CompletedTask;
        }
    }
}
