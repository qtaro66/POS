namespace Pos.Application.Security;

public sealed record BranchEmployeeSessionLoginResult(
    string BranchId,
    string DeviceId,
    string EmployeeId,
    string SessionId,
    string Token,
    DateTimeOffset ExpiresAt);

public sealed class BranchEmployeeSessionLoginService
{
    private readonly BranchEmployeePinAuthenticationService _pinAuthentication;
    private readonly BranchEmployeeSessionIssuanceService _sessionIssuance;

    public BranchEmployeeSessionLoginService(
        BranchEmployeePinAuthenticationService pinAuthentication,
        BranchEmployeeSessionIssuanceService sessionIssuance)
    {
        _pinAuthentication = pinAuthentication ??
            throw new ArgumentNullException(nameof(pinAuthentication));
        _sessionIssuance = sessionIssuance ??
            throw new ArgumentNullException(nameof(sessionIssuance));
    }

    public async Task<BranchEmployeeSessionLoginResult?> LoginAsync(
        string branchId,
        string deviceId,
        ReadOnlyMemory<char> presentedPin,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        deviceId = RequireIdentifier(deviceId, nameof(deviceId));

        var employee = await _pinAuthentication.AuthenticateAsync(
            branchId,
            presentedPin,
            cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var issued = await _sessionIssuance.IssueAsync(
            branchId,
            deviceId,
            employee.EmployeeId,
            cancellationToken);
        return new BranchEmployeeSessionLoginResult(
            branchId,
            deviceId,
            employee.EmployeeId,
            issued.SessionId,
            issued.Token,
            issued.ExpiresAt);
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The identifier cannot be empty.",
                parameterName)
            : value.Trim();
}
