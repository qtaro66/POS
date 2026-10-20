using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingRedemptionHttpHandlerTests
{
    private static readonly DateTimeOffset RedeemedAt =
        DateTimeOffset.Parse("2026-10-20T04:00:00Z");

    [Fact]
    public async Task Valid_challenge_returns_200_with_permanent_device_credential()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(redeemed: true);
        var httpResponse = Response();

        var result = await BranchPairingRedemptionHttpHandler.HandleAsync(
            Request(pairing),
            Service(store),
            httpResponse,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchPairingRedemptionResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        AssertSecretResponseHeaders(httpResponse);
        var response = Assert.IsType<BranchPairingRedemptionResponse>(ok.Value);
        Assert.Equal(1, response.SchemaVersion);
        Assert.True(BranchDeviceIdentifierFormat.IsCanonical(response.DeviceId));
        Assert.True(BranchDeviceCredentialFormat.TryParse(
            response.DeviceCredential,
            out var parsedCredential));
        parsedCredential.Dispose();
        var registration = Assert.IsType<BranchPairingRedemptionRegistration>(
            store.Registration);
        Assert.Equal("BRANCH-01", registration.BranchId);
        Assert.Equal(pairing.PairingId, registration.PairingId);
        Assert.Equal("Dining Tablet 01", registration.DeviceDisplayName);
        Assert.Equal(RedeemedAt, registration.RedeemedAt);
        Assert.True(BranchPairingTokenFormat.TryParse(pairing.Token, out var parsedPairing));
        using (var expected = Assert.IsType<ParsedBranchPairingToken>(parsedPairing))
        {
            Assert.Equal(expected.Secret.ToArray(), store.PresentedSecret);
        }
    }

    [Theory]
    [InlineData(0, "BRANCH-01", "Dining Tablet 01")]
    [InlineData(2, "BRANCH-01", "Dining Tablet 01")]
    [InlineData(1, "", "Dining Tablet 01")]
    [InlineData(1, " ", "Dining Tablet 01")]
    [InlineData(1, "BRANCH-01", "")]
    [InlineData(1, "BRANCH-01", " ")]
    public async Task Unsupported_schema_or_missing_public_field_returns_400_without_store_access(
        int schemaVersion,
        string branchId,
        string displayName)
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(redeemed: true);
        var request = Request(pairing) with
        {
            SchemaVersion = schemaVersion,
            BranchId = branchId,
            DeviceDisplayName = displayName
        };

        var result = await BranchPairingRedemptionHttpHandler.HandleAsync(
            request,
            Service(store),
            Response(),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Null(store.Registration);
    }

    [Fact]
    public async Task Malformed_pairing_identity_returns_same_401_without_store_access()
    {
        var store = new RecordingStore(redeemed: true);
        var httpResponse = Response();
        var request = new BranchPairingRedemptionRequest(
            1,
            "BRANCH-01",
            "not-canonical",
            "v1.invalid",
            "Dining Tablet 01");

        var result = await BranchPairingRedemptionHttpHandler.HandleAsync(
            request,
            Service(store),
            httpResponse,
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        AssertSecretResponseHeaders(httpResponse);
        Assert.Null(store.Registration);
    }

    [Fact]
    public async Task Rejected_challenge_returns_same_401_after_store_validation()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(redeemed: false);

        var result = await BranchPairingRedemptionHttpHandler.HandleAsync(
            Request(pairing),
            Service(store),
            Response(),
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.NotNull(store.Registration);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_redemption_store()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(redeemed: false);
        using var source = new CancellationTokenSource();

        await BranchPairingRedemptionHttpHandler.HandleAsync(
            Request(pairing),
            Service(store),
            Response(),
            source.Token);

        Assert.Equal(source.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Storage_failure_is_not_recast_as_unauthorized()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(
            new InvalidOperationException("Synthetic persistence failure."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchPairingRedemptionHttpHandler.HandleAsync(
                Request(pairing),
                Service(store),
                Response(),
                CancellationToken.None));

        Assert.Equal("Synthetic persistence failure.", exception.Message);
    }

    private static BranchPairingRedemptionRequest Request(
        IssuedBranchPairingToken pairing) =>
        new(
            1,
            " BRANCH-01 ",
            pairing.PairingId,
            pairing.Token,
            " Dining Tablet 01 ");

    private static BranchPairingRedemptionService Service(RecordingStore store) =>
        new(store, new FixedTimeProvider(RedeemedAt));

    private static HttpResponse Response() => new DefaultHttpContext().Response;

    private static void AssertSecretResponseHeaders(HttpResponse response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl);
        Assert.Equal("no-cache", response.Headers.Pragma);
    }

    private sealed class RecordingStore : IBranchPairingRedemptionStore
    {
        private readonly bool _redeemed;
        private readonly Exception? _exception;

        public RecordingStore(bool redeemed)
        {
            _redeemed = redeemed;
        }

        public RecordingStore(Exception exception)
        {
            _exception = exception;
        }

        public BranchPairingRedemptionRegistration? Registration { get; private set; }

        public byte[]? PresentedSecret { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<bool> TryRedeemAsync(
            BranchPairingRedemptionRegistration registration,
            ReadOnlyMemory<byte> presentedPairingSecret,
            CancellationToken cancellationToken = default)
        {
            Registration = registration;
            PresentedSecret = presentedPairingSecret.ToArray();
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_redeemed)
                : Task.FromException<bool>(_exception);
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
