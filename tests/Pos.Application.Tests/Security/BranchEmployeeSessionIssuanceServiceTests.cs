using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchEmployeeSessionIssuanceServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 20, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Issue_persists_hash_and_returns_plaintext_token_after_success()
    {
        var store = new RecordingStore();
        var service = Service(store, Now.AddHours(8));

        var result = await service.IssueAsync(
            " BRANCH-01 ",
            " DEVICE-01 ",
            " EMPLOYEE-01 ",
            CancellationToken.None);

        var registration = Assert.IsType<BranchEmployeeSessionRegistration>(
            store.Registration);
        Assert.Equal("BRANCH-01", registration.BranchId);
        Assert.Equal("DEVICE-01", registration.DeviceId);
        Assert.Equal("EMPLOYEE-01", registration.EmployeeId);
        Assert.Equal(result.SessionId, registration.SessionId);
        Assert.Equal(Now, registration.IssuedAt);
        Assert.Equal(Now.AddHours(8), registration.ExpiresAt);
        Assert.Equal(registration.ExpiresAt, result.ExpiresAt);
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(
            result.Token,
            out var parsed));
        using (var token = Assert.IsType<ParsedBranchEmployeeSessionToken>(parsed))
        {
            Assert.Equal(result.SessionId, token.SessionId);
            Assert.True(BranchEmployeeSessionTokenFormat.VerifySecretHash(
                registration.CopySecretHash(),
                token.Secret.Span));
        }
    }

    [Fact]
    public async Task Sequential_issuance_creates_distinct_sessions_in_same_scope()
    {
        var store = new RecordingStore();
        var service = Service(store, Now.AddHours(8));

        var first = await service.IssueAsync(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01");
        var second = await service.IssueAsync(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01");

        Assert.Equal(2, store.CallCount);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.NotEqual(first.Token, second.Token);
        Assert.All(
            store.Registrations,
            registration =>
            {
                Assert.Equal("BRANCH-01", registration.BranchId);
                Assert.Equal("DEVICE-01", registration.DeviceId);
                Assert.Equal("EMPLOYEE-01", registration.EmployeeId);
            });
    }

    [Fact]
    public async Task Store_failure_is_propagated_without_returning_session()
    {
        var store = new RecordingStore(
            new InvalidOperationException("Synthetic persistence failure."));
        var service = Service(store, Now.AddHours(8));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueAsync("BRANCH-01", "DEVICE-01", "EMPLOYEE-01"));

        Assert.Equal("Synthetic persistence failure.", exception.Message);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        using var source = new CancellationTokenSource();
        var store = new RecordingStore();
        var service = Service(store, Now.AddHours(8));

        await service.IssueAsync(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            source.Token);

        Assert.Equal(source.Token, store.CancellationToken);
    }

    [Theory]
    [InlineData("", "DEVICE-01", "EMPLOYEE-01")]
    [InlineData(" ", "DEVICE-01", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", "", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", " ", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", "DEVICE-01", "")]
    [InlineData("BRANCH-01", "DEVICE-01", " ")]
    public async Task Empty_identity_is_rejected_before_policy_and_store(
        string branchId,
        string deviceId,
        string employeeId)
    {
        var store = new RecordingStore();
        var policy = new FixedLifetimePolicy(Now.AddHours(8));
        var service = new BranchEmployeeSessionIssuanceService(
            store,
            policy,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IssueAsync(branchId, deviceId, employeeId));

        Assert.Equal(0, policy.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [MemberData(nameof(InvalidExpiryValues))]
    public async Task Invalid_lifetime_policy_result_is_rejected_before_store(
        DateTimeOffset expiresAt)
    {
        var store = new RecordingStore();
        var service = Service(store, expiresAt);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueAsync("BRANCH-01", "DEVICE-01", "EMPLOYEE-01"));

        Assert.Equal(
            "The employee session lifetime policy returned an invalid expiry.",
            exception.Message);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public void Registration_rejects_invalid_lifecycle_data()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var hash = issued.CopySecretHash();

        Assert.Throws<ArgumentException>(() => new BranchEmployeeSessionRegistration(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            issued.SessionId,
            Now.ToOffset(TimeSpan.FromHours(7)),
            Now.AddHours(8),
            hash));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchEmployeeSessionRegistration(
                "BRANCH-01",
                "DEVICE-01",
                "EMPLOYEE-01",
                issued.SessionId,
                Now,
                Now,
                hash));
        Assert.Throws<ArgumentException>(() => new BranchEmployeeSessionRegistration(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            "not-canonical",
            Now,
            Now.AddHours(8),
            hash));
        Assert.Throws<ArgumentException>(() => new BranchEmployeeSessionRegistration(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            issued.SessionId,
            Now,
            Now.AddHours(8),
            new byte[31]));
    }

    [Fact]
    public void Registration_returns_defensive_hash_copies()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var sourceHash = issued.CopySecretHash();
        var registration = new BranchEmployeeSessionRegistration(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            issued.SessionId,
            Now,
            Now.AddHours(8),
            sourceHash);
        sourceHash[0] ^= byte.MaxValue;

        var first = registration.CopySecretHash();
        first[0] ^= byte.MaxValue;
        var second = registration.CopySecretHash();

        Assert.NotEqual(sourceHash, second);
        Assert.NotEqual(first, second);
    }

    public static TheoryData<DateTimeOffset> InvalidExpiryValues => new()
    {
        Now,
        Now.AddTicks(-1),
        Now.AddHours(8).ToOffset(TimeSpan.FromHours(7))
    };

    private static BranchEmployeeSessionIssuanceService Service(
        RecordingStore store,
        DateTimeOffset expiresAt) =>
        new(
            store,
            new FixedLifetimePolicy(expiresAt),
            new FixedTimeProvider(Now));

    private sealed class RecordingStore : IBranchEmployeeSessionIssuanceStore
    {
        private readonly Exception? _exception;

        public RecordingStore(Exception? exception = null)
        {
            _exception = exception;
        }

        public List<BranchEmployeeSessionRegistration> Registrations { get; } = [];

        public BranchEmployeeSessionRegistration? Registration =>
            Registrations.LastOrDefault();

        public int CallCount { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task CreateAsync(
            BranchEmployeeSessionRegistration registration,
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

    private sealed class FixedLifetimePolicy : IBranchEmployeeSessionLifetimePolicy
    {
        private readonly DateTimeOffset _expiresAt;

        public FixedLifetimePolicy(DateTimeOffset expiresAt)
        {
            _expiresAt = expiresAt;
        }

        public int CallCount { get; private set; }

        public DateTimeOffset GetExpiresAt(DateTimeOffset issuedAt)
        {
            Assert.Equal(Now, issuedAt);
            CallCount++;
            return _expiresAt;
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
