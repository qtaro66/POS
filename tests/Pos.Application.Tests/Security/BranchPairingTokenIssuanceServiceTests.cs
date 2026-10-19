using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchPairingTokenIssuanceServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 19, 9, 55, 0, TimeSpan.Zero);

    [Fact]
    public async Task Issue_persists_hash_and_returns_five_minute_plaintext_challenge()
    {
        var store = new RecordingStore();
        var service = new BranchPairingTokenIssuanceService(
            store,
            new FixedTimeProvider(Now));

        var result = await service.IssueAsync(
            " BRANCH-01 ",
            " EMPLOYEE-01 ",
            CancellationToken.None);

        var registration = Assert.IsType<BranchPairingTokenRegistration>(store.Registration);
        Assert.Equal("BRANCH-01", registration.BranchId);
        Assert.Equal("EMPLOYEE-01", registration.CreatedByEmployeeId);
        Assert.Equal(result.PairingId, registration.PairingId);
        Assert.Equal(Now, registration.IssuedAt);
        Assert.Equal(Now.AddMinutes(5), registration.ExpiresAt);
        Assert.Equal(registration.ExpiresAt, result.ExpiresAt);
        Assert.True(BranchPairingTokenFormat.TryParse(result.Token, out var parsed));
        using (var token = Assert.IsType<ParsedBranchPairingToken>(parsed))
        {
            Assert.True(BranchPairingTokenFormat.VerifySecretHash(
                registration.CopySecretHash(),
                token.Secret.Span));
        }
    }

    [Fact]
    public async Task Sequential_issuance_uses_same_creator_scope_and_distinct_tokens()
    {
        var store = new RecordingStore();
        var service = new BranchPairingTokenIssuanceService(
            store,
            new FixedTimeProvider(Now));

        var first = await service.IssueAsync("BRANCH-01", "EMPLOYEE-01");
        var second = await service.IssueAsync("BRANCH-01", "EMPLOYEE-01");

        Assert.Equal(2, store.CallCount);
        Assert.All(
            store.Registrations,
            registration =>
            {
                Assert.Equal("BRANCH-01", registration.BranchId);
                Assert.Equal("EMPLOYEE-01", registration.CreatedByEmployeeId);
            });
        Assert.NotEqual(first.PairingId, second.PairingId);
        Assert.NotEqual(first.Token, second.Token);
    }

    [Fact]
    public async Task Store_failure_is_propagated_without_returning_challenge()
    {
        var store = new RecordingStore(
            new InvalidOperationException("Synthetic persistence failure."));
        var service = new BranchPairingTokenIssuanceService(
            store,
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueAsync("BRANCH-01", "EMPLOYEE-01"));

        Assert.Equal("Synthetic persistence failure.", exception.Message);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        using var source = new CancellationTokenSource();
        var store = new RecordingStore();
        var service = new BranchPairingTokenIssuanceService(
            store,
            new FixedTimeProvider(Now));

        await service.IssueAsync("BRANCH-01", "EMPLOYEE-01", source.Token);

        Assert.Equal(source.Token, store.CancellationToken);
    }

    [Theory]
    [InlineData("", "EMPLOYEE-01")]
    [InlineData(" ", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", "")]
    [InlineData("BRANCH-01", " ")]
    public async Task Empty_identity_is_rejected_before_store(
        string branchId,
        string employeeId)
    {
        var store = new RecordingStore();
        var service = new BranchPairingTokenIssuanceService(
            store,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IssueAsync(branchId, employeeId));

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public void Registration_rejects_non_utc_or_invalid_lifecycle_data()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var hash = issued.CopySecretHash();

        Assert.Throws<ArgumentException>(() => new BranchPairingTokenRegistration(
            "BRANCH-01",
            issued.PairingId,
            "EMPLOYEE-01",
            Now.ToOffset(TimeSpan.FromHours(7)),
            Now.AddMinutes(5),
            hash));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchPairingTokenRegistration(
                "BRANCH-01",
                issued.PairingId,
                "EMPLOYEE-01",
                Now,
                Now,
                hash));
        Assert.Throws<ArgumentException>(() => new BranchPairingTokenRegistration(
            "BRANCH-01",
            "not-canonical",
            "EMPLOYEE-01",
            Now,
            Now.AddMinutes(5),
            hash));
        Assert.Throws<ArgumentException>(() => new BranchPairingTokenRegistration(
            "BRANCH-01",
            issued.PairingId,
            "EMPLOYEE-01",
            Now,
            Now.AddMinutes(5),
            new byte[31]));
    }

    [Fact]
    public void Registration_returns_defensive_hash_copies()
    {
        var issued = BranchPairingTokenFormat.Generate();
        var sourceHash = issued.CopySecretHash();
        var registration = new BranchPairingTokenRegistration(
            "BRANCH-01",
            issued.PairingId,
            "EMPLOYEE-01",
            Now,
            Now.AddMinutes(5),
            sourceHash);
        sourceHash[0] ^= byte.MaxValue;

        var first = registration.CopySecretHash();
        first[0] ^= byte.MaxValue;
        var second = registration.CopySecretHash();

        Assert.NotEqual(sourceHash, second);
        Assert.NotEqual(first, second);
    }

    private sealed class RecordingStore : IBranchPairingTokenIssuanceStore
    {
        private readonly Exception? _exception;

        public RecordingStore(Exception? exception = null)
        {
            _exception = exception;
        }

        public List<BranchPairingTokenRegistration> Registrations { get; } = [];

        public BranchPairingTokenRegistration? Registration => Registrations.LastOrDefault();

        public int CallCount { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task ReplaceActiveForCreatorAsync(
            BranchPairingTokenRegistration registration,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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
