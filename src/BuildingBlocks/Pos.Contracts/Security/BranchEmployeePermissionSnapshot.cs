namespace Pos.Contracts.Security;

public sealed record BranchEmployeePermissionSnapshot(
    int SchemaVersion,
    string BranchId,
    long Revision,
    IReadOnlyList<BranchEmployeePermissionEntry> Employees);

public sealed record BranchEmployeePermissionEntry(string EmployeeId, bool Active, bool SetSoldOut);
