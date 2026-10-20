using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingIssuanceHttpHandlerTests
{
    private static readonly DateTimeOffset IssuedAt =
        DateTimeOffset.Parse("2026-10-19T12:00:00Z");

    [Fact]
    public async Task Authenticated_identity_issues_200_challenge_for_its_branch_and_employee()
    {
        var store = new RecordingStore();
        var service = Service(store);
        var httpResponse = Response();

        var result = await BranchPairingIssuanceHttpHandler.HandleAsync(
            Identity("BRANCH-01", "DEVICE-01", "EMPLOYEE-01"),
            Options("https://192.168.2.2:8443"),
            service,
            httpResponse,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchPairingChallengeResponse>>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        AssertSecretResponseHeaders(httpResponse);
        var response = Assert.IsType<BranchPairingChallengeResponse>(ok.Value);
        Assert.Equal("https://192.168.2.2:8443/", response.BranchServiceUrl);
        Assert.Equal("BRANCH-01", response.BranchId);
        Assert.Equal(IssuedAt.AddMinutes(5), response.ExpiresAt);
        Assert.True(BranchPairingTokenFormat.IsCanonicalPairingId(response.PairingId));
        Assert.True(BranchPairingTokenFormat.TryParse(response.OneTimeToken, out var parsed));
        using (var token = Assert.IsType<ParsedBranchPairingToken>(parsed))
        {
            var registration = Assert.Single(store.Registrations);
            Assert.Equal("BRANCH-01", registration.BranchId);
            Assert.Equal("EMPLOYEE-01", registration.CreatedByEmployeeId);
            Assert.Equal(response.PairingId, registration.PairingId);
            Assert.True(BranchPairingTokenFormat.VerifySecretHash(
                registration.CopySecretHash(),
                token.Secret.Span));
        }
    }

    [Fact]
    public async Task Invalid_service_origin_is_rejected_before_issuing_challenge()
    {
        var store = new RecordingStore();
        var httpResponse = Response();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            BranchPairingIssuanceHttpHandler.HandleAsync(
                Identity("BRANCH-01", "DEVICE-01", "EMPLOYEE-01"),
                Options("http://192.168.2.2"),
                Service(store),
                httpResponse,
                CancellationToken.None));

        AssertSecretResponseHeaders(httpResponse);
        Assert.Empty(store.Registrations);
    }

    [Fact]
    public async Task Missing_identity_is_rejected_before_issuing_challenge()
    {
        var store = new RecordingStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchPairingIssuanceHttpHandler.HandleAsync(
                null!,
                Options("https://192.168.2.2"),
                Service(store),
                Response(),
                CancellationToken.None));

        Assert.Empty(store.Registrations);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_issuance_store()
    {
        using var source = new CancellationTokenSource();
        var store = new RecordingStore();

        await BranchPairingIssuanceHttpHandler.HandleAsync(
            Identity("BRANCH-01", "DEVICE-01", "EMPLOYEE-01"),
            Options("https://192.168.2.2"),
            Service(store),
            Response(),
            source.Token);

        Assert.Equal(source.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Storage_failure_is_not_recast_as_http_success()
    {
        var store = new RecordingStore(
            new InvalidOperationException("Synthetic persistence failure."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchPairingIssuanceHttpHandler.HandleAsync(
                Identity("BRANCH-01", "DEVICE-01", "EMPLOYEE-01"),
                Options("https://192.168.2.2"),
                Service(store),
                Response(),
                CancellationToken.None));

        Assert.Equal("Synthetic persistence failure.", exception.Message);
    }

    private static BranchPairingTokenIssuanceService Service(RecordingStore store) =>
        new(store, new FixedTimeProvider(IssuedAt));

    private static IOptions<BranchPairingOptions> Options(string serviceOrigin) =>
        Microsoft.Extensions.Options.Options.Create(new BranchPairingOptions
        {
            ServiceOrigin = serviceOrigin
        });

    private static HttpResponse Response() => new DefaultHttpContext().Response;

    private static void AssertSecretResponseHeaders(HttpResponse response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl);
        Assert.Equal("no-cache", response.Headers.Pragma);
    }

    private static AuthenticatedBranchRequestIdentity Identity(
        string branchId,
        string deviceId,
        string employeeId)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(BranchPosClaimTypes.BranchId, branchId),
                new Claim(BranchPosClaimTypes.DeviceId, deviceId),
                new Claim(BranchPosClaimTypes.EmployeeId, employeeId)
            ],
            "TestAuthentication"));
        Assert.True(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                principal,
                out var identity));
        return identity!;
    }

    private sealed class RecordingStore : IBranchPairingTokenIssuanceStore
    {
        private readonly Exception? _exception;

        public RecordingStore(Exception? exception = null)
        {
            _exception = exception;
        }

        public List<BranchPairingTokenRegistration> Registrations { get; } = [];

        public CancellationToken CancellationToken { get; private set; }

        public Task ReplaceActiveForCreatorAsync(
            BranchPairingTokenRegistration registration,
            CancellationToken cancellationToken = default)
        {
            Registrations.Add(registration);
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.CompletedTask
                : Task.FromException(_exception);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
