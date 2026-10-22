namespace Pos.Contracts.Security;

public sealed record BranchEmployeeSessionLoginResponse(
    int SchemaVersion,
    string EmployeeId,
    string SessionId,
    string SessionToken,
    DateTimeOffset ExpiresAt);
