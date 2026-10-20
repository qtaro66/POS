using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchEmployeeSessionAuthenticationServiceTests
{
    [Fact]
    public async Task Active_session_returns_verified_identity_and_clears_decoded_secret()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var validator = new RecordingValidator(
            new VerifiedBranchEmployeeSession(
                "BRANCH-01",
                "DEVICE-01",
                "EMPLOYEE-01"));
        var service = new BranchEmployeeSessionAuthenticationService(validator);
        var presentedToken = issued.Token.ToCharArray();

        var result = await service.AuthenticateAsync(
            " BRANCH-01 ",
            " DEVICE-01 ",
            presentedToken,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal("DEVICE-01", result.DeviceId);
        Assert.Equal("EMPLOYEE-01", result.EmployeeId);
        Assert.Equal("BRANCH-01", validator.BranchId);
        Assert.Equal("DEVICE-01", validator.DeviceId);
        Assert.Equal(issued.SessionId, validator.SessionId);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(issued.Token, presentedToken);
    }

    [Theory]
    [InlineData("not-a-session-token")]
    [InlineData("es2.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Malformed_token_returns_unauthenticated_without_validator(
        string token)
    {
        var validator = new RecordingValidator((VerifiedBranchEmployeeSession?)null);
        var service = new BranchEmployeeSessionAuthenticationService(validator);

        var result = await service.AuthenticateAsync(
            "BRANCH-01",
            "DEVICE-01",
            token.AsMemory());

        Assert.Null(result);
        Assert.Null(validator.BranchId);
        Assert.Null(validator.CapturedSecret);
    }

    [Fact]
    public async Task Invalid_inactive_or_revoked_session_returns_one_unauthenticated_result()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var validator = new RecordingValidator((VerifiedBranchEmployeeSession?)null);
        var service = new BranchEmployeeSessionAuthenticationService(validator);

        var result = await service.AuthenticateAsync(
            "BRANCH-01",
            "DEVICE-01",
            issued.Token.AsMemory());

        Assert.Null(result);
        Assert.Equal(issued.SessionId, validator.SessionId);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Validator_failure_clears_decoded_secret_and_propagates()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var validator = new RecordingValidator(
            new InvalidOperationException("Synthetic validation failure."));
        var service = new BranchEmployeeSessionAuthenticationService(validator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AuthenticateAsync(
                "BRANCH-01",
                "DEVICE-01",
                issued.Token.AsMemory()));

        Assert.Equal("Synthetic validation failure.", exception.Message);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData("BRANCH-02", "DEVICE-01")]
    [InlineData("BRANCH-01", "DEVICE-02")]
    public async Task Mismatched_validator_identity_is_rejected(
        string verifiedBranchId,
        string verifiedDeviceId)
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var validator = new RecordingValidator(
            new VerifiedBranchEmployeeSession(
                verifiedBranchId,
                verifiedDeviceId,
                "EMPLOYEE-01"));
        var service = new BranchEmployeeSessionAuthenticationService(validator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AuthenticateAsync(
                "BRANCH-01",
                "DEVICE-01",
                issued.Token.AsMemory()));

        Assert.Equal(
            "The verified employee session belongs to a different Branch or device.",
            exception.Message);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        using var source = new CancellationTokenSource();
        var validator = new RecordingValidator((VerifiedBranchEmployeeSession?)null);
        var service = new BranchEmployeeSessionAuthenticationService(validator);

        await service.AuthenticateAsync(
            "BRANCH-01",
            "DEVICE-01",
            issued.Token.AsMemory(),
            source.Token);

        Assert.Equal(source.Token, validator.CancellationToken);
    }

    [Theory]
    [InlineData("", "DEVICE-01")]
    [InlineData(" ", "DEVICE-01")]
    [InlineData("BRANCH-01", "")]
    [InlineData("BRANCH-01", " ")]
    public async Task Empty_trusted_identifier_is_rejected_before_validator(
        string branchId,
        string deviceId)
    {
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var validator = new RecordingValidator((VerifiedBranchEmployeeSession?)null);
        var service = new BranchEmployeeSessionAuthenticationService(validator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuthenticateAsync(
                branchId,
                deviceId,
                issued.Token.AsMemory()));

        Assert.Null(validator.BranchId);
    }

    [Fact]
    public async Task Empty_session_token_is_rejected_before_validator()
    {
        var validator = new RecordingValidator((VerifiedBranchEmployeeSession?)null);
        var service = new BranchEmployeeSessionAuthenticationService(validator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuthenticateAsync(
                "BRANCH-01",
                "DEVICE-01",
                ReadOnlyMemory<char>.Empty));

        Assert.Null(validator.BranchId);
    }

    [Theory]
    [InlineData("", "DEVICE-01", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", " ", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", "DEVICE-01", "")]
    public void Verified_session_requires_all_identifiers(
        string branchId,
        string deviceId,
        string employeeId)
    {
        Assert.Throws<ArgumentException>(() =>
            new VerifiedBranchEmployeeSession(branchId, deviceId, employeeId));
    }

    private sealed class RecordingValidator : IBranchEmployeeSessionValidator
    {
        private readonly VerifiedBranchEmployeeSession? _result;
        private readonly Exception? _exception;

        public RecordingValidator(VerifiedBranchEmployeeSession? result)
        {
            _result = result;
        }

        public RecordingValidator(Exception exception)
        {
            _exception = exception;
        }

        public string? BranchId { get; private set; }

        public string? DeviceId { get; private set; }

        public string? SessionId { get; private set; }

        public ReadOnlyMemory<byte>? CapturedSecret { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<VerifiedBranchEmployeeSession?> ValidateActiveAsync(
            string branchId,
            string deviceId,
            string sessionId,
            ReadOnlyMemory<byte> presentedSecret,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            DeviceId = deviceId;
            SessionId = sessionId;
            CapturedSecret = presentedSecret;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_result)
                : Task.FromException<VerifiedBranchEmployeeSession?>(_exception);
        }
    }
}
