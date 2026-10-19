using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchDeviceCredentialAuthenticationServiceTests
{
    [Fact]
    public async Task Active_credential_returns_verified_device_and_clears_working_secret()
    {
        var validator = new RecordingValidator(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
        var service = new BranchDeviceCredentialAuthenticationService(validator);
        var presentedSecret = new byte[] { 11, 22, 33, 44 };

        var result = await service.AuthenticateAsync(
            " CREDENTIAL-01 ",
            presentedSecret,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal("DEVICE-01", result.DeviceId);
        Assert.Equal("CREDENTIAL-01", validator.CredentialId);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(new byte[] { 11, 22, 33, 44 }, presentedSecret);
    }

    [Fact]
    public async Task Invalid_or_inactive_credential_returns_one_unauthenticated_result()
    {
        var validator = new RecordingValidator((VerifiedBranchDevice?)null);
        var service = new BranchDeviceCredentialAuthenticationService(validator);

        var result = await service.AuthenticateAsync(
            "CREDENTIAL-01",
            new byte[] { 1, 2, 3 },
            CancellationToken.None);

        Assert.Null(result);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Validator_failure_clears_working_secret_and_propagates()
    {
        var validator = new RecordingValidator(
            new InvalidOperationException("Synthetic validation failure."));
        var service = new BranchDeviceCredentialAuthenticationService(validator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AuthenticateAsync(
                "CREDENTIAL-01",
                new byte[] { 5, 6, 7 },
                CancellationToken.None));

        Assert.Equal("Synthetic validation failure.", exception.Message);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        using var source = new CancellationTokenSource();
        var validator = new RecordingValidator((VerifiedBranchDevice?)null);
        var service = new BranchDeviceCredentialAuthenticationService(validator);

        await service.AuthenticateAsync(
            "CREDENTIAL-01",
            new byte[] { 1 },
            source.Token);

        Assert.Equal(source.Token, validator.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Empty_credential_id_is_rejected_before_validator(string credentialId)
    {
        var validator = new RecordingValidator((VerifiedBranchDevice?)null);
        var service = new BranchDeviceCredentialAuthenticationService(validator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuthenticateAsync(
                credentialId,
                new byte[] { 1 },
                CancellationToken.None));

        Assert.Null(validator.CredentialId);
    }

    [Fact]
    public async Task Empty_secret_is_rejected_before_validator()
    {
        var validator = new RecordingValidator((VerifiedBranchDevice?)null);
        var service = new BranchDeviceCredentialAuthenticationService(validator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuthenticateAsync(
                "CREDENTIAL-01",
                ReadOnlyMemory<byte>.Empty,
                CancellationToken.None));

        Assert.Null(validator.CredentialId);
    }

    [Theory]
    [InlineData("", "DEVICE-01")]
    [InlineData("BRANCH-01", " ")]
    public void Verified_device_requires_both_identifiers(
        string branchId,
        string deviceId)
    {
        Assert.Throws<ArgumentException>(() =>
            new VerifiedBranchDevice(branchId, deviceId));
    }

    private sealed class RecordingValidator : IBranchDeviceCredentialValidator
    {
        private readonly VerifiedBranchDevice? _result;
        private readonly Exception? _exception;

        public RecordingValidator(VerifiedBranchDevice? result)
        {
            _result = result;
        }

        public RecordingValidator(Exception exception)
        {
            _exception = exception;
        }

        public string? CredentialId { get; private set; }

        public ReadOnlyMemory<byte>? CapturedSecret { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<VerifiedBranchDevice?> ValidateActiveAsync(
            string credentialId,
            ReadOnlyMemory<byte> credentialSecret,
            CancellationToken cancellationToken = default)
        {
            CredentialId = credentialId;
            CapturedSecret = credentialSecret;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_result)
                : Task.FromException<VerifiedBranchDevice?>(_exception);
        }
    }
}
