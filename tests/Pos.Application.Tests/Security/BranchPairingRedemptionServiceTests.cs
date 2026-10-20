using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchPairingRedemptionServiceTests
{
    private static readonly DateTimeOffset RedeemedAt =
        DateTimeOffset.Parse("2026-10-20T03:00:00Z");

    [Fact]
    public async Task Valid_challenge_returns_new_device_and_permanent_credential()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(true);
        var service = Service(store);

        var result = await service.RedeemAsync(
            " BRANCH-01 ",
            pairing.PairingId,
            pairing.Token,
            " Dining Tablet 01 ");

        var redeemed = Assert.IsType<RedeemedBranchDevice>(result);
        Assert.True(BranchDeviceIdentifierFormat.IsCanonical(redeemed.DeviceId));
        Assert.True(BranchDeviceCredentialFormat.TryParse(
            redeemed.DeviceCredential,
            out var parsedCredential));
        using (var credential = Assert.IsType<ParsedBranchDeviceCredential>(parsedCredential))
        {
            var registration = Assert.IsType<BranchPairingRedemptionRegistration>(
                store.Registration);
            Assert.Equal("BRANCH-01", registration.BranchId);
            Assert.Equal(pairing.PairingId, registration.PairingId);
            Assert.Equal(redeemed.DeviceId, registration.DeviceId);
            Assert.Equal("Dining Tablet 01", registration.DeviceDisplayName);
            Assert.Equal(credential.CredentialId, registration.CredentialId);
            Assert.Equal(RedeemedAt, registration.RedeemedAt);
            Assert.True(BranchDeviceCredentialFormat.VerifySecretHash(
                registration.CopyCredentialSecretHash(),
                credential.Secret.Span));
        }

        Assert.True(BranchPairingTokenFormat.TryParse(pairing.Token, out var parsedPairing));
        using (var token = Assert.IsType<ParsedBranchPairingToken>(parsedPairing))
        {
            Assert.Equal(token.Secret.ToArray(), store.PresentedSecretSnapshot);
        }
    }

    [Theory]
    [InlineData("not-canonical", "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA", "v1.invalid")]
    public async Task Malformed_challenge_returns_one_null_result_without_store_access(
        string pairingId,
        string token)
    {
        var store = new RecordingStore(true);

        var result = await Service(store).RedeemAsync(
            "BRANCH-01",
            pairingId,
            token,
            "Tablet 01");

        Assert.Null(result);
        Assert.Null(store.Registration);
    }

    [Fact]
    public async Task Store_rejection_returns_no_device_or_credential()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(false);

        var result = await Service(store).RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Tablet 01");

        Assert.Null(result);
        Assert.NotNull(store.Registration);
    }

    [Fact]
    public async Task Parsed_pairing_secret_is_zeroed_after_store_returns()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(true);

        await Service(store).RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Tablet 01");

        Assert.NotNull(store.PresentedSecretMemory);
        Assert.All(
            store.PresentedSecretMemory.Value.ToArray(),
            value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_atomic_store()
    {
        using var source = new CancellationTokenSource();
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(true);

        await Service(store).RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Tablet 01",
            source.Token);

        Assert.Equal(source.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Store_failure_propagates_without_returning_credential()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var store = new RecordingStore(
            new InvalidOperationException("Synthetic redemption failure."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store).RedeemAsync(
                "BRANCH-01",
                pairing.PairingId,
                pairing.Token,
                "Tablet 01"));

        Assert.Equal("Synthetic redemption failure.", exception.Message);
    }

    [Fact]
    public async Task Separate_redemptions_generate_distinct_device_and_credential_identities()
    {
        var firstPairing = BranchPairingTokenFormat.Generate();
        var secondPairing = BranchPairingTokenFormat.Generate();
        var first = await Service(new RecordingStore(true)).RedeemAsync(
            "BRANCH-01",
            firstPairing.PairingId,
            firstPairing.Token,
            "Tablet 01");
        var second = await Service(new RecordingStore(true)).RedeemAsync(
            "BRANCH-01",
            secondPairing.PairingId,
            secondPairing.Token,
            "Tablet 02");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.DeviceId, second.DeviceId);
        Assert.NotEqual(first.DeviceCredential, second.DeviceCredential);
    }

    [Fact]
    public void Registration_returns_defensive_credential_hash_copies()
    {
        var pairing = BranchPairingTokenFormat.Generate();
        var credential = BranchDeviceCredentialFormat.Generate();
        var registration = new BranchPairingRedemptionRegistration(
            "BRANCH-01",
            pairing.PairingId,
            BranchDeviceIdentifierFormat.Generate(),
            "Tablet 01",
            credential.CredentialId,
            RedeemedAt,
            credential.CopySecretHash());
        var first = registration.CopyCredentialSecretHash();
        first[0] ^= byte.MaxValue;

        Assert.NotEqual(first, registration.CopyCredentialSecretHash());
    }

    private static BranchPairingRedemptionService Service(IBranchPairingRedemptionStore store) =>
        new(store, new FixedTimeProvider(RedeemedAt));

    private sealed class RecordingStore : IBranchPairingRedemptionStore
    {
        private readonly bool _result;
        private readonly Exception? _exception;

        public RecordingStore(bool result)
        {
            _result = result;
        }

        public RecordingStore(Exception exception)
        {
            _exception = exception;
        }

        public BranchPairingRedemptionRegistration? Registration { get; private set; }

        public ReadOnlyMemory<byte>? PresentedSecretMemory { get; private set; }

        public byte[]? PresentedSecretSnapshot { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<bool> TryRedeemAsync(
            BranchPairingRedemptionRegistration registration,
            ReadOnlyMemory<byte> presentedPairingSecret,
            CancellationToken cancellationToken = default)
        {
            Registration = registration;
            PresentedSecretMemory = presentedPairingSecret;
            PresentedSecretSnapshot = presentedPairingSecret.ToArray();
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_result)
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
