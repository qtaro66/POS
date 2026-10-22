using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionLoginResponseMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchEmployeeSessionLoginResponse Map(
        BranchEmployeeSessionLoginResult login)
    {
        ArgumentNullException.ThrowIfNull(login);
        RequireIdentifier(login.BranchId, nameof(login), "Branch");
        RequireIdentifier(login.DeviceId, nameof(login), "device");
        var employeeId = RequireIdentifier(login.EmployeeId, nameof(login));
        if (!BranchEmployeeSessionTokenFormat.IsCanonicalSessionId(login.SessionId))
        {
            throw new ArgumentException(
                "The employee session identifier is not canonical.",
                nameof(login));
        }

        if (!BranchEmployeeSessionTokenFormat.TryParse(
                login.Token,
                out var parsedToken))
        {
            throw new ArgumentException(
                "The employee session token is not canonical.",
                nameof(login));
        }

        using (parsedToken)
        {
            if (!string.Equals(
                    parsedToken.SessionId,
                    login.SessionId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The employee session token does not match its session identifier.",
                    nameof(login));
            }
        }

        if (login.ExpiresAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The employee session expiry must be UTC.",
                nameof(login));
        }

        return new BranchEmployeeSessionLoginResponse(
            CurrentSchemaVersion,
            employeeId,
            login.SessionId,
            login.Token,
            login.ExpiresAt);
    }

    private static string RequireIdentifier(
        string value,
        string parameterName,
        string label = "employee") =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                $"The {label} identifier cannot be empty.",
                parameterName)
            : value.Trim();
}
