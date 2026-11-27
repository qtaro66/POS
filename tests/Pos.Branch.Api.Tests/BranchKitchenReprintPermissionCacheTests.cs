using System.Text;
using System.Text.Json;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchKitchenReprintPermissionCacheTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "reprint-permissions-" + Guid.NewGuid().ToString("N") + ".json");
    private Task<bool> Apply(BranchEmployeePermissionCache cache, string json) =>
        ApplyBytes(cache, Encoding.UTF8.GetBytes(json));
    private static async Task<bool> ApplyBytes(BranchEmployeePermissionCache cache, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await cache.ApplyAsync(stream, CancellationToken.None);
    }
    private Task<bool> Apply(BranchEmployeePermissionCache cache, long revision, bool permitted, bool active = true,
        string branch = "B1") => ApplyBytes(cache, JsonSerializer.SerializeToUtf8Bytes(
            new BranchEmployeePermissionSnapshot(1, branch, revision, [new("E1", active, true, true, permitted)]),
            JsonSerializerOptions.Web));

    [Fact]
    public async Task Old_snapshot_defaults_to_denied_and_new_revision_is_scoped_restored_and_revoked()
    {
        var cache = new BranchEmployeePermissionCache("B1", _path);
        Assert.False(cache.CanReprintKitchenOrder("B1", "E1"));
        Assert.True(await Apply(cache, """{"schemaVersion":1,"branchId":"B1","revision":1,"employees":[{"employeeId":"E1","active":true,"setSoldOut":true}]}"""));
        Assert.True(cache.CanSetSoldOut("B1", "E1"));
        Assert.False(cache.CanOpenStore("B1", "E1"));
        Assert.False(cache.CanReprintKitchenOrder("B1", "E1"));
        Assert.True(await Apply(cache, 2, true));
        Assert.True(cache.CanReprintKitchenOrder("B1", "E1"));
        Assert.True(cache.CanSetSoldOut("B1", "E1")); Assert.True(cache.CanOpenStore("B1", "E1"));
        Assert.False(cache.CanReprintKitchenOrder("B2", "E1"));
        Assert.False(cache.CanReprintKitchenOrder("B1", "OTHER"));
        Assert.False(cache.CanReprintKitchenOrder("B1", "e1"));
        Assert.False(await Apply(cache, 3, true, branch: "B2"));
        Assert.False(await Apply(cache, 1, false)); Assert.False(await Apply(cache, 2, false));
        Assert.True(cache.CanReprintKitchenOrder("B1", "E1"));
        var restarted = new BranchEmployeePermissionCache("B1", _path);
        await restarted.RestoreAsync(CancellationToken.None);
        Assert.True(restarted.CanReprintKitchenOrder("B1", "E1"));
        Assert.True(await Apply(restarted, 3, false));
        Assert.False(restarted.CanReprintKitchenOrder("B1", "E1"));
        Assert.True(await Apply(restarted, 4, true, active: false));
        Assert.False(restarted.CanReprintKitchenOrder("B1", "E1"));
        Assert.False(restarted.CanSetSoldOut("B1", "E1")); Assert.False(restarted.CanOpenStore("B1", "E1"));
        Assert.True(await Apply(restarted, """{"schemaVersion":1,"branchId":"B1","revision":5,"employees":[{"employeeId":"E1","active":true,"setSoldOut":true,"openStore":true}]}"""));
        Assert.False(restarted.CanReprintKitchenOrder("B1", "E1"));
    }

    [Theory]
    [InlineData("\"reprintKitchenOrder\":false,\"reprintKitchenOrder\":true")]
    [InlineData("\"reprintKitchenOrder\":false,\"ReprintKitchenOrder\":true")]
    [InlineData("\"active\":true,\"Active\":false,\"reprintKitchenOrder\":true")]
    public async Task Ambiguous_employee_properties_never_replace_last_good_snapshot(string fields)
    {
        var cache = new BranchEmployeePermissionCache("B1", _path); Assert.True(await Apply(cache, 1, false));
        var before = await File.ReadAllBytesAsync(_path);
        Assert.False(await Apply(cache, """{"schemaVersion":1,"branchId":"B1","revision":2,"employees":[{"employeeId":"E1","active":true,"setSoldOut":false,""" + fields + "}]}"));
        Assert.False(cache.CanReprintKitchenOrder("B1", "E1"));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Fact]
    public async Task Restore_of_legacy_snapshot_does_not_inherit_reprint_from_other_permissions()
    {
        await File.WriteAllTextAsync(_path,
            """{"schemaVersion":1,"branchId":"B1","revision":7,"employees":[{"employeeId":"E1","active":true,"setSoldOut":true,"openStore":true}]}""");
        var cache = new BranchEmployeePermissionCache("B1", _path);
        await cache.RestoreAsync(CancellationToken.None);
        Assert.True(cache.CanOpenStore("B1", "E1")); Assert.True(cache.CanSetSoldOut("B1", "E1"));
        Assert.False(cache.CanReprintKitchenOrder("B1", "E1"));
    }

    [Fact]
    public async Task Ambiguous_root_fields_are_rejected_and_cannot_grant_on_restore()
    {
        const string json = """{"schemaVersion":1,"branchId":"B1","revision":1,"Revision":2,"employees":[{"employeeId":"E1","active":true,"setSoldOut":false,"reprintKitchenOrder":true}]}""";
        var cache = new BranchEmployeePermissionCache("B1", _path);
        Assert.False(await Apply(cache, json));
        Assert.False(File.Exists(_path));
        await File.WriteAllTextAsync(_path, json);
        await cache.RestoreAsync(CancellationToken.None);
        Assert.False(cache.CanReprintKitchenOrder("B1", "E1"));
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("null")]
    [InlineData("1")]
    public async Task Nonboolean_permission_never_replaces_last_good_snapshot(string value)
    {
        var cache = new BranchEmployeePermissionCache("B1", _path); Assert.True(await Apply(cache, 1, true));
        var before = await File.ReadAllBytesAsync(_path);
        await Assert.ThrowsAsync<JsonException>(() => Apply(cache,
            """{"schemaVersion":1,"branchId":"B1","revision":2,"employees":[{"employeeId":"E1","active":true,"setSoldOut":false,"reprintKitchenOrder":""" + value + "}]}"));
        Assert.True(cache.CanReprintKitchenOrder("B1", "E1"));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    public void Dispose() => File.Delete(_path);
}
