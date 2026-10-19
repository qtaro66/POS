using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchDeviceTokenAuthenticationServiceTests
{
    [Fact]
    public async Task Valid_token_returns_verified_device_and_clears_validator_working_secret()
    {
        var issued = BranchDeviceCredentialFormat.Generate();
        var validator = new RecordingValidator(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
        var service = new BranchDeviceTokenAuthenticationService(validator);

        var result = await service.AuthenticateAsync(issued.Token);

        var device = Assert.IsType<VerifiedBranchDevice>(result);
        Assert.Equal("BRANCH-01", device.BranchId);
        Assert.Equal("DEVICE-01", device.DeviceId);
        Assert.Equal(issued.CredentialId, validator.CredentialId);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.invalid")]
    [InlineData("v2.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Malformed_token_fails_closed_without_calling_validator(string? token)
    {
        var validator = new RecordingValidator(
            new VerifiedBranchDevice("BRANCH-01", "DEVICE-01"));
        var service = new BranchDeviceTokenAuthenticationService(validator);

        var result = await service.AuthenticateAsync(token);

        Assert.Null(result);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public async Task Inactive_credential_returns_one_unauthenticated_result()
    {
        var issued = BranchDeviceCredentialFormat.Generate();
        var validator = new RecordingValidator(result: null);
        var service = new BranchDeviceTokenAuthenticationService(validator);

        var result = await service.AuthenticateAsync(issued.Token);

        Assert.Null(result);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_and_working_secret_is_cleared()
    {
        var issued = BranchDeviceCredentialFormat.Generate();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var validator = new RecordingValidator(
            result: null,
            exception: new OperationCanceledException(cancellation.Token));
        var service = new BranchDeviceTokenAuthenticationService(validator);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AuthenticateAsync(issued.Token, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(cancellation.Token, validator.CancellationToken);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Validator_failure_is_propagated_without_retaining_working_secret()
    {
        var issued = BranchDeviceCredentialFormat.Generate();
        var validator = new RecordingValidator(
            result: null,
            exception: new InvalidOperationException("Synthetic validator failure."));
        var service = new BranchDeviceTokenAuthenticationService(validator);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AuthenticateAsync(issued.Token));

        Assert.Equal("Synthetic validator failure.", error.Message);
        Assert.NotNull(validator.CapturedSecret);
        Assert.All(validator.CapturedSecret.Value.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Null_validator_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BranchDeviceTokenAuthenticationService(null!));
    }

    private sealed class RecordingValidator : IBranchDeviceCredentialValidator
    {
        private readonly VerifiedBranchDevice? _result;
        private readonly Exception? _exception;

        public RecordingValidator(
            VerifiedBranchDevice? result,
            Exception? exception = null)
        {
            _result = result;
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public string? CredentialId { get; private set; }

        public ReadOnlyMemory<byte>? CapturedSecret { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<VerifiedBranchDevice?> ValidateActiveAsync(
            string credentialId,
            ReadOnlyMemory<byte> credentialSecret,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CredentialId = credentialId;
            CapturedSecret = credentialSecret;
            CancellationToken = cancellationToken;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result);
        }
    }
}
