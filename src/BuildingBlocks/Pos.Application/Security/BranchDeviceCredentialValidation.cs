using System.Security.Cryptography;

namespace Pos.Application.Security;

public sealed class VerifiedBranchDevice
{
    public VerifiedBranchDevice(string branchId, string deviceId)
    {
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        DeviceId = RequireIdentifier(deviceId, nameof(deviceId));
    }

    public string BranchId { get; }

    public string DeviceId { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}

public interface IBranchDeviceCredentialValidator
{
    Task<VerifiedBranchDevice?> ValidateActiveAsync(
        string credentialId,
        ReadOnlyMemory<byte> credentialSecret,
        CancellationToken cancellationToken = default);
}

public sealed class BranchDeviceCredentialAuthenticationService
{
    private readonly IBranchDeviceCredentialValidator _validator;

    public BranchDeviceCredentialAuthenticationService(
        IBranchDeviceCredentialValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<VerifiedBranchDevice?> AuthenticateAsync(
        string credentialId,
        ReadOnlyMemory<byte> presentedSecret,
        CancellationToken cancellationToken = default)
    {
        credentialId = RequireCredentialId(credentialId);
        if (presentedSecret.IsEmpty)
        {
            throw new ArgumentException(
                "The credential secret cannot be empty.",
                nameof(presentedSecret));
        }

        var workingSecret = presentedSecret.ToArray();
        try
        {
            return await _validator.ValidateActiveAsync(
                credentialId,
                workingSecret,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(workingSecret);
        }
    }

    private static string RequireCredentialId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The credential identifier cannot be empty.",
                nameof(value))
            : value.Trim();
}
