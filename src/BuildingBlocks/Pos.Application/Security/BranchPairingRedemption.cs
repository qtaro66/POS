namespace Pos.Application.Security;

public sealed class BranchPairingRedemptionRegistration
{
    private readonly byte[] _credentialSecretHash;

    public BranchPairingRedemptionRegistration(
        string branchId,
        string pairingId,
        string deviceId,
        string deviceDisplayName,
        string credentialId,
        DateTimeOffset redeemedAt,
        ReadOnlySpan<byte> credentialSecretHash)
    {
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        PairingId = BranchPairingTokenFormat.IsCanonicalPairingId(pairingId)
            ? pairingId
            : throw new ArgumentException(
                "The pairing identifier is not canonical.",
                nameof(pairingId));
        DeviceId = BranchDeviceIdentifierFormat.IsCanonical(deviceId)
            ? deviceId
            : throw new ArgumentException(
                "The device identifier is not canonical.",
                nameof(deviceId));
        DeviceDisplayName = RequireIdentifier(
            deviceDisplayName,
            nameof(deviceDisplayName));
        CredentialId = BranchDeviceCredentialFormat.IsCanonicalCredentialId(credentialId)
            ? credentialId
            : throw new ArgumentException(
                "The credential identifier is not canonical.",
                nameof(credentialId));
        RedeemedAt = RequireUtc(redeemedAt, nameof(redeemedAt));
        if (credentialSecretHash.Length != BranchDeviceCredentialFormat.SecretHashByteLength)
        {
            throw new ArgumentException(
                "The device credential secret hash has an unexpected length.",
                nameof(credentialSecretHash));
        }

        _credentialSecretHash = credentialSecretHash.ToArray();
    }

    public string BranchId { get; }

    public string PairingId { get; }

    public string DeviceId { get; }

    public string DeviceDisplayName { get; }

    public string CredentialId { get; }

    public DateTimeOffset RedeemedAt { get; }

    public byte[] CopyCredentialSecretHash() => _credentialSecretHash.ToArray();

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();

    private static DateTimeOffset RequireUtc(
        DateTimeOffset value,
        string parameterName) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("The timestamp must be UTC.", parameterName);
}

public sealed record RedeemedBranchDevice(
    string DeviceId,
    string DeviceCredential);

public interface IBranchPairingRedemptionStore
{
    /// <remarks>
    /// The implementation must finish using <paramref name="presentedPairingSecret"/>
    /// before the returned task completes and must not retain the memory.
    /// </remarks>
    Task<bool> TryRedeemAsync(
        BranchPairingRedemptionRegistration registration,
        ReadOnlyMemory<byte> presentedPairingSecret,
        CancellationToken cancellationToken = default);
}

public sealed class BranchPairingRedemptionService
{
    private readonly IBranchPairingRedemptionStore _store;
    private readonly TimeProvider _timeProvider;

    public BranchPairingRedemptionService(
        IBranchPairingRedemptionStore store,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RedeemedBranchDevice?> RedeemAsync(
        string branchId,
        string pairingId,
        string presentedPairingToken,
        string deviceDisplayName,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        deviceDisplayName = RequireIdentifier(
            deviceDisplayName,
            nameof(deviceDisplayName));
        if (!BranchPairingTokenFormat.IsCanonicalPairingId(pairingId) ||
            !BranchPairingTokenFormat.TryParse(
                presentedPairingToken,
                out var parsedPairingToken))
        {
            return null;
        }

        using (parsedPairingToken)
        {
            var issuedCredential = BranchDeviceCredentialFormat.Generate();
            var registration = new BranchPairingRedemptionRegistration(
                branchId,
                pairingId,
                BranchDeviceIdentifierFormat.Generate(),
                deviceDisplayName,
                issuedCredential.CredentialId,
                _timeProvider.GetUtcNow().ToUniversalTime(),
                issuedCredential.CopySecretHash());

            var redeemed = await _store.TryRedeemAsync(
                registration,
                parsedPairingToken.Secret,
                cancellationToken);
            return redeemed
                ? new RedeemedBranchDevice(
                    registration.DeviceId,
                    issuedCredential.Token)
                : null;
        }
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
