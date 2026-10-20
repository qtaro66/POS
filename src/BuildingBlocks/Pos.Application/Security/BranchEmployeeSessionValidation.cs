using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Pos.Application.Security;

public sealed class VerifiedBranchEmployeeSession
{
    public VerifiedBranchEmployeeSession(
        string branchId,
        string deviceId,
        string employeeId)
    {
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        DeviceId = RequireIdentifier(deviceId, nameof(deviceId));
        EmployeeId = RequireIdentifier(employeeId, nameof(employeeId));
    }

    public string BranchId { get; }

    public string DeviceId { get; }

    public string EmployeeId { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}

public interface IBranchEmployeeSessionValidator
{
    Task<VerifiedBranchEmployeeSession?> ValidateActiveAsync(
        string branchId,
        string deviceId,
        ReadOnlyMemory<char> sessionToken,
        CancellationToken cancellationToken = default);
}

public sealed class BranchEmployeeSessionAuthenticationService
{
    private readonly IBranchEmployeeSessionValidator _validator;

    public BranchEmployeeSessionAuthenticationService(
        IBranchEmployeeSessionValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<VerifiedBranchEmployeeSession?> AuthenticateAsync(
        string branchId,
        string deviceId,
        ReadOnlyMemory<char> presentedToken,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        deviceId = RequireIdentifier(deviceId, nameof(deviceId));
        if (presentedToken.IsEmpty)
        {
            throw new ArgumentException(
                "The employee session token cannot be empty.",
                nameof(presentedToken));
        }

        var workingToken = presentedToken.ToArray();
        try
        {
            var session = await _validator.ValidateActiveAsync(
                branchId,
                deviceId,
                workingToken,
                cancellationToken);
            if (session is not null &&
                (!string.Equals(session.BranchId, branchId, StringComparison.Ordinal) ||
                 !string.Equals(session.DeviceId, deviceId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "The verified employee session belongs to a different Branch or device.");
            }

            return session;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                MemoryMarshal.AsBytes(workingToken.AsSpan()));
        }
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}
