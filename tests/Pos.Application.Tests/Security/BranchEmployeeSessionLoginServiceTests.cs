using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchEmployeeSessionLoginServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Active_employee_receives_session_only_after_durable_registration()
    {
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore();
        var service = Service(pinValidator, store);
        var pin = "1234".ToCharArray();

        var result = await service.LoginAsync(
            " BRANCH-01 ",
            " DEVICE-01 ",
            pin);

        var login = Assert.IsType<BranchEmployeeSessionLoginResult>(result);
        Assert.Equal("BRANCH-01", login.BranchId);
        Assert.Equal("DEVICE-01", login.DeviceId);
        Assert.Equal("EMPLOYEE-01", login.EmployeeId);
        Assert.Equal(Now.AddHours(8), login.ExpiresAt);
        Assert.Equal("1234", pin);
        Assert.Equal("1234", pinValidator.PresentedPin);
        var registration = Assert.IsType<BranchEmployeeSessionRegistration>(
            store.Registration);
        Assert.Equal(login.SessionId, registration.SessionId);
        Assert.Equal("BRANCH-01", registration.BranchId);
        Assert.Equal("DEVICE-01", registration.DeviceId);
        Assert.Equal("EMPLOYEE-01", registration.EmployeeId);
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(
            login.Token,
            out var parsed));
        using (parsed)
        {
            Assert.Equal(login.SessionId, parsed.SessionId);
            Assert.True(BranchEmployeeSessionTokenFormat.VerifySecretHash(
                registration.CopySecretHash(),
                parsed.Secret.Span));
        }
    }

    [Fact]
    public async Task Invalid_pin_returns_one_unauthenticated_result_without_issuance()
    {
        var pinValidator = new RecordingPinValidator(result: null);
        var store = new RecordingSessionStore();
        var service = Service(pinValidator, store);

        var result = await service.LoginAsync(
            "BRANCH-01",
            "DEVICE-01",
            "wrong".AsMemory());

        Assert.Null(result);
        Assert.Equal(1, pinValidator.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Session_store_failure_is_propagated_without_login_result()
    {
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore(
            new IOException("Synthetic session storage failure."));
        var service = Service(pinValidator, store);

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            service.LoginAsync(
                "BRANCH-01",
                "DEVICE-01",
                "1234".AsMemory()));

        Assert.Equal("Synthetic session storage failure.", exception.Message);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_pin_validation_and_session_storage()
    {
        using var source = new CancellationTokenSource();
        var pinValidator = new RecordingPinValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var store = new RecordingSessionStore();
        var service = Service(pinValidator, store);

        await service.LoginAsync(
            "BRANCH-01",
            "DEVICE-01",
            "1234".AsMemory(),
            source.Token);

        Assert.Equal(source.Token, pinValidator.CancellationToken);
        Assert.Equal(source.Token, store.CancellationToken);
    }

    [Theory]
    [InlineData("", "DEVICE-01")]
    [InlineData(" ", "DEVICE-01")]
    [InlineData("BRANCH-01", "")]
    [InlineData("BRANCH-01", " ")]
    public async Task Empty_trusted_identity_is_rejected_before_pin_validation(
        string branchId,
        string deviceId)
    {
        var pinValidator = new RecordingPinValidator(result: null);
        var store = new RecordingSessionStore();
        var service = Service(pinValidator, store);

        await Assert.ThrowsAsync<ArgumentException>(() => service.LoginAsync(
            branchId,
            deviceId,
            "1234".AsMemory()));

        Assert.Equal(0, pinValidator.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Empty_pin_is_rejected_without_issuance()
    {
        var pinValidator = new RecordingPinValidator(result: null);
        var store = new RecordingSessionStore();
        var service = Service(pinValidator, store);

        await Assert.ThrowsAsync<ArgumentException>(() => service.LoginAsync(
            "BRANCH-01",
            "DEVICE-01",
            ReadOnlyMemory<char>.Empty));

        Assert.Equal(0, pinValidator.CallCount);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public void Constructor_requires_both_authentication_and_issuance_services()
    {
        var pinAuthentication = new BranchEmployeePinAuthenticationService(
            new RecordingPinValidator(result: null));
        var issuance = Issuance(new RecordingSessionStore());

        Assert.Throws<ArgumentNullException>(() =>
            new BranchEmployeeSessionLoginService(null!, issuance));
        Assert.Throws<ArgumentNullException>(() =>
            new BranchEmployeeSessionLoginService(pinAuthentication, null!));
    }

    private static BranchEmployeeSessionLoginService Service(
        IBranchEmployeePinValidator pinValidator,
        RecordingSessionStore store) =>
        new(
            new BranchEmployeePinAuthenticationService(pinValidator),
            Issuance(store));

    private static BranchEmployeeSessionIssuanceService Issuance(
        RecordingSessionStore store) =>
        new(
            store,
            new FixedBranchEmployeeSessionLifetimePolicy(TimeSpan.FromHours(8)),
            new FixedTimeProvider(Now));

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

        public string? PresentedPin { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(
            string branchId,
            ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            PresentedPin = new string(pin.Span);
            CancellationToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingSessionStore :
        IBranchEmployeeSessionIssuanceStore
    {
        private readonly Exception? _exception;

        public RecordingSessionStore(Exception? exception = null)
        {
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public BranchEmployeeSessionRegistration? Registration { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task CreateAsync(
            BranchEmployeeSessionRegistration registration,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Registration = registration;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.CompletedTask
                : Task.FromException(_exception);
        }
    }
}
