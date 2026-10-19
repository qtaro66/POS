namespace Pos.Application.Security;

public sealed class BranchPairingTokenRegistration
{
    private readonly byte[] _secretHash;

    public BranchPairingTokenRegistration(
        string branchId,
        string pairingId,
        string createdByEmployeeId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        ReadOnlySpan<byte> secretHash)
    {
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        PairingId = BranchPairingTokenFormat.IsCanonicalPairingId(pairingId)
            ? pairingId
            : throw new ArgumentException(
                "The pairing identifier is not canonical.",
                nameof(pairingId));
        CreatedByEmployeeId = RequireIdentifier(
            createdByEmployeeId,
            nameof(createdByEmployeeId));
        IssuedAt = RequireUtc(issuedAt, nameof(issuedAt));
        ExpiresAt = RequireUtc(expiresAt, nameof(expiresAt));
        if (ExpiresAt <= IssuedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "The expiry must be later than issuance.");
        }

        if (secretHash.Length != BranchPairingTokenFormat.SecretHashByteLength)
        {
            throw new ArgumentException(
                "The pairing secret hash has an unexpected length.",
                nameof(secretHash));
        }

        _secretHash = secretHash.ToArray();
    }

    public string BranchId { get; }

    public string PairingId { get; }

    public string CreatedByEmployeeId { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public byte[] CopySecretHash() => _secretHash.ToArray();

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

public sealed record IssuedBranchPairingChallenge(
    string PairingId,
    string Token,
    DateTimeOffset ExpiresAt);

public interface IBranchPairingTokenIssuanceStore
{
    Task ReplaceActiveForCreatorAsync(
        BranchPairingTokenRegistration registration,
        CancellationToken cancellationToken = default);
}

public sealed class BranchPairingTokenIssuanceService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IBranchPairingTokenIssuanceStore _store;
    private readonly TimeProvider _timeProvider;

    public BranchPairingTokenIssuanceService(
        IBranchPairingTokenIssuanceStore store,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IssuedBranchPairingChallenge> IssueAsync(
        string branchId,
        string createdByEmployeeId,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        createdByEmployeeId = RequireIdentifier(
            createdByEmployeeId,
            nameof(createdByEmployeeId));

        var issuedAt = _timeProvider.GetUtcNow().ToUniversalTime();
        var expiresAt = issuedAt.Add(Lifetime);
        var issued = BranchPairingTokenFormat.Generate();
        var registration = new BranchPairingTokenRegistration(
            branchId,
            issued.PairingId,
            createdByEmployeeId,
            issuedAt,
            expiresAt,
            issued.CopySecretHash());

        await _store.ReplaceActiveForCreatorAsync(registration, cancellationToken);

        return new IssuedBranchPairingChallenge(
            issued.PairingId,
            issued.Token,
            expiresAt);
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
