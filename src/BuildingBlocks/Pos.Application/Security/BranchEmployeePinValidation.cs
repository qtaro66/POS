using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Pos.Application.Security;

public sealed class VerifiedBranchEmployee
{
    public VerifiedBranchEmployee(string branchId, string employeeId)
    {
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        EmployeeId = RequireIdentifier(employeeId, nameof(employeeId));
    }

    public string BranchId { get; }

    public string EmployeeId { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}

public interface IBranchEmployeePinValidator
{
    Task<VerifiedBranchEmployee?> ValidateActiveAsync(
        string branchId,
        ReadOnlyMemory<char> pin,
        CancellationToken cancellationToken = default);
}

public sealed class BranchEmployeePinAuthenticationService
{
    private readonly IBranchEmployeePinValidator _validator;

    public BranchEmployeePinAuthenticationService(
        IBranchEmployeePinValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<VerifiedBranchEmployee?> AuthenticateAsync(
        string branchId,
        ReadOnlyMemory<char> presentedPin,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireBranchId(branchId);
        if (presentedPin.IsEmpty)
        {
            throw new ArgumentException("The PIN cannot be empty.", nameof(presentedPin));
        }

        var workingPin = presentedPin.ToArray();
        try
        {
            var employee = await _validator.ValidateActiveAsync(
                branchId,
                workingPin,
                cancellationToken);
            if (employee is not null &&
                !string.Equals(employee.BranchId, branchId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The verified employee belongs to a different Branch.");
            }

            return employee;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                MemoryMarshal.AsBytes(workingPin.AsSpan()));
        }
    }

    private static string RequireBranchId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The Branch identifier cannot be empty.",
                nameof(value))
            : value.Trim();
}
