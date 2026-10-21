namespace Pos.Application.Security;

public sealed class BranchEmployeeSessionRegistration
{
    private readonly byte[] _secretHash;

    public BranchEmployeeSessionRegistration(
        string branchId,
        string deviceId,
        string employeeId,
        string sessionId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        ReadOnlySpan<byte> secretHash)
    {
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        DeviceId = RequireIdentifier(deviceId, nameof(deviceId));
        EmployeeId = RequireIdentifier(employeeId, nameof(employeeId));
        SessionId = BranchEmployeeSessionTokenFormat.IsCanonicalSessionId(sessionId)
            ? sessionId
            : throw new ArgumentException(
                "The employee session identifier is not canonical.",
                nameof(sessionId));
        IssuedAt = RequireUtc(issuedAt, nameof(issuedAt));
        ExpiresAt = RequireUtc(expiresAt, nameof(expiresAt));
        if (ExpiresAt <= IssuedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "The expiry must be later than issuance.");
        }

        if (secretHash.Length != BranchEmployeeSessionTokenFormat.SecretHashByteLength)
        {
            throw new ArgumentException(
                "The employee session secret hash has an unexpected length.",
                nameof(secretHash));
        }

        _secretHash = secretHash.ToArray();
    }

    public string BranchId { get; }

    public string DeviceId { get; }

    public string EmployeeId { get; }

    public string SessionId { get; }

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

public sealed record IssuedBranchEmployeeSession(
    string SessionId,
    string Token,
    DateTimeOffset ExpiresAt);

public interface IBranchEmployeeSessionIssuanceStore
{
    Task CreateAsync(
        BranchEmployeeSessionRegistration registration,
        CancellationToken cancellationToken = default);
}

public interface IBranchEmployeeSessionLifetimePolicy
{
    DateTimeOffset GetExpiresAt(DateTimeOffset issuedAt);
}

public sealed class BranchEmployeeSessionIssuanceService
{
    private readonly IBranchEmployeeSessionIssuanceStore _store;
    private readonly IBranchEmployeeSessionLifetimePolicy _lifetimePolicy;
    private readonly TimeProvider _timeProvider;

    public BranchEmployeeSessionIssuanceService(
        IBranchEmployeeSessionIssuanceStore store,
        IBranchEmployeeSessionLifetimePolicy lifetimePolicy,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lifetimePolicy = lifetimePolicy ??
            throw new ArgumentNullException(nameof(lifetimePolicy));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IssuedBranchEmployeeSession> IssueAsync(
        string branchId,
        string deviceId,
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        deviceId = RequireIdentifier(deviceId, nameof(deviceId));
        employeeId = RequireIdentifier(employeeId, nameof(employeeId));

        var issuedAt = _timeProvider.GetUtcNow().ToUniversalTime();
        var expiresAt = _lifetimePolicy.GetExpiresAt(issuedAt);
        if (expiresAt.Offset != TimeSpan.Zero || expiresAt <= issuedAt)
        {
            throw new InvalidOperationException(
                "The employee session lifetime policy returned an invalid expiry.");
        }

        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var registration = new BranchEmployeeSessionRegistration(
            branchId,
            deviceId,
            employeeId,
            issued.SessionId,
            issuedAt,
            expiresAt,
            issued.CopySecretHash());

        await _store.CreateAsync(registration, cancellationToken);

        return new IssuedBranchEmployeeSession(
            issued.SessionId,
            issued.Token,
            expiresAt);
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
