namespace Pos.Contracts.Security;

public sealed record BranchEmployeeSessionLoginRequest(
    int SchemaVersion,
    string Pin);
