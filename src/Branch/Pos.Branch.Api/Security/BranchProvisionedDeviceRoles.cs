using Pos.Application.Security;

namespace Pos.Branch.Api.Security;

public sealed class BranchProvisionedDeviceRoles(IConfiguration configuration)
{
    public string? Resolve(VerifiedBranchDevice device)
    {
        var assignments = configuration.GetSection("BranchDeviceAuthentication:ProvisionedRoles").GetChildren()
            .Where(entry => string.Equals(entry["BranchId"], device.BranchId, StringComparison.Ordinal)
                && string.Equals(entry["DeviceId"], device.DeviceId, StringComparison.Ordinal))
            .Take(2).ToArray();
        return assignments.Length == 1 && assignments[0]["Role"] is "PcPos" or "Mobile"
            ? assignments[0]["Role"]
            : null;
    }
}
