using System.Text.Json;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeePermissionCacheTests
{
    [Fact]
    public async Task Restores_latest_revision_and_refuses_cross_branch_and_stale_snapshots()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cache = new BranchEmployeePermissionCache("B1", path);
            var mobile = BranchSoldOutAdjustmentHttpHandlerTests.Principal("Mobile", null, true);
            Assert.True(AuthenticatedBranchRequestIdentityResolver.TryResolve(mobile, out var identity));
            Assert.NotNull(identity);
            Assert.False(BranchSoldOutAuthorization.CanAdjust(mobile, identity, cache));
            Assert.False(cache.CanSetSoldOut("B1", "E1"));
            Assert.True(await Apply(cache, new(1, "B1", 2, [new("E1", true, true), new("EMP1", true, true)])));
            Assert.True(BranchSoldOutAuthorization.CanAdjust(mobile, identity, cache));
            Assert.True(cache.CanSetSoldOut("B1", "E1"));
            Assert.False(cache.CanSetSoldOut("B2", "E1"));
            Assert.False(await Apply(cache, new(1, "B2", 9, [])));
            Assert.False(await Apply(cache, new(1, "B1", 1, [])));

            var restarted = new BranchEmployeePermissionCache("B1", path);
            await restarted.RestoreAsync(CancellationToken.None);
            Assert.True(restarted.CanSetSoldOut("B1", "E1"));
            Assert.True(BranchSoldOutAuthorization.CanAdjust(mobile, identity, restarted));
            Assert.True(await Apply(restarted, new(1, "B1", 3, [new("E1", false, true), new("EMP1", false, true)])));
            Assert.False(BranchSoldOutAuthorization.CanAdjust(mobile, identity, restarted));
            Assert.False(restarted.CanSetSoldOut("B1", "E1"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<bool> Apply(BranchEmployeePermissionCache cache, BranchEmployeePermissionSnapshot snapshot)
    {
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return await cache.ApplyAsync(stream, CancellationToken.None);
    }
}
