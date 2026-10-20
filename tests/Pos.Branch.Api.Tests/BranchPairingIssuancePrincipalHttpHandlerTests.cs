using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingIssuancePrincipalHttpHandlerTests
{
    [Fact]
    public async Task Complete_authenticated_identity_issues_challenge_from_claims()
    {
        var store = new RecordingStore();
        var httpResponse = new DefaultHttpContext().Response;

        var result = await BranchPairingIssuancePrincipalHttpHandler.HandleAsync(
            Principal(includeEmployee: true, authenticated: true),
            PairingOptions(),
            new BranchPairingTokenIssuanceService(store, TimeProvider.System),
            httpResponse,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchPairingChallengeResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var registration = Assert.IsType<BranchPairingTokenRegistration>(
            store.Registration);
        Assert.Equal("BRANCH-01", registration.BranchId);
        Assert.Equal("EMPLOYEE-01", registration.CreatedByEmployeeId);
        Assert.Equal("no-store", httpResponse.Headers.CacheControl);
        Assert.Equal("no-cache", httpResponse.Headers.Pragma);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Incomplete_or_unauthenticated_identity_returns_401_without_issuance(
        bool includeEmployee,
        bool authenticated)
    {
        var store = new RecordingStore();
        var httpResponse = new DefaultHttpContext().Response;

        var result = await BranchPairingIssuancePrincipalHttpHandler.HandleAsync(
            Principal(includeEmployee, authenticated),
            PairingOptions(),
            new BranchPairingTokenIssuanceService(store, TimeProvider.System),
            httpResponse,
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Null(store.Registration);
        Assert.Equal("no-store", httpResponse.Headers.CacheControl);
        Assert.Equal("no-cache", httpResponse.Headers.Pragma);
    }

    [Fact]
    public async Task Missing_principal_returns_same_401_without_issuance()
    {
        var store = new RecordingStore();

        var result = await BranchPairingIssuancePrincipalHttpHandler.HandleAsync(
            null,
            PairingOptions(),
            new BranchPairingTokenIssuanceService(store, TimeProvider.System),
            new DefaultHttpContext().Response,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Null(store.Registration);
    }

    private static ClaimsPrincipal Principal(bool includeEmployee, bool authenticated)
    {
        var claims = new List<Claim>
        {
            new(BranchPosClaimTypes.BranchId, "BRANCH-01"),
            new(BranchPosClaimTypes.DeviceId, "DEVICE-01")
        };
        if (includeEmployee)
        {
            claims.Add(new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticated ? "SyntheticAuthentication" : null));
    }

    private static IOptions<BranchPairingOptions> PairingOptions() =>
        Options.Create(new BranchPairingOptions
        {
            ServiceOrigin = "https://192.0.2.10:8443"
        });

    private sealed class RecordingStore : IBranchPairingTokenIssuanceStore
    {
        public BranchPairingTokenRegistration? Registration { get; private set; }

        public Task ReplaceActiveForCreatorAsync(
            BranchPairingTokenRegistration registration,
            CancellationToken cancellationToken = default)
        {
            Registration = registration;
            return Task.CompletedTask;
        }
    }
}
