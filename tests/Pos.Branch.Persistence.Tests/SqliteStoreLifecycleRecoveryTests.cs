using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteStoreLifecycleRecoveryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lifecycle-recovery-{Guid.NewGuid():N}.db");
    private static StoreOperationContext Actor(string employee = "employee", string device = "device") =>
        new(employee, device, new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero), new(2026, 11, 20));
    private static StoreLifecycleCommand Open() => new("request", "store", "shift", StoreLifecycleAction.OpenStore, 500, Actor());
    private static StoreLifecycleRequestTarget Target(StoreLifecycleCommand command) =>
        new(command.RequestId, command.StoreSessionId, command.ShiftId, command.Action, command.CashAmount);
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public async Task Status_recovers_saved_outcome_without_original_employee_and_does_not_change_store()
    {
        var store = await Store();
        var open = Open();
        var saved = await store.ExecuteStoreLifecycleAsync("branch", open);
        var result = await (await Store()).ReadStoreLifecycleRequestAsync("branch", "device", Target(open));
        Assert.Equal("Completed", result.Status);
        Assert.Equal(saved, result.Result);
        Assert.Equal(result, await store.RetireStoreLifecycleRequestAsync("branch", "device", Target(open), Actor("other")));
        Assert.Equal(saved, await store.ReadStoreLifecycleAsync("branch", "store"));
    }

    [Fact]
    public async Task Retirement_persists_actor_and_prevents_late_submission_without_changing_business_state()
    {
        var store = await Store();
        var command = Open();
        var target = Target(command);
        Assert.Equal("NotFound", (await store.ReadStoreLifecycleRequestAsync("branch", "device", target)).Status);
        var retired = await store.RetireStoreLifecycleRequestAsync("branch", "device", target, Actor("retiring-employee"));
        Assert.Equal("Retired", retired.Status);
        Assert.Equal(retired, await (await Store()).ReadStoreLifecycleRequestAsync("branch", "device", target));
        Assert.Equal(retired, await store.RetireStoreLifecycleRequestAsync("branch", "device", target, Actor("other")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteStoreLifecycleAsync("branch", command));
        Assert.Null(await store.ReadCurrentStoreLifecycleAsync("branch"));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT retired_by_employee_id || ':' || device_id FROM store_lifecycle_retirements;";
        Assert.Equal("retiring-employee:device", await read.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Completed_and_retired_targets_require_exact_tuple_and_owning_device()
    {
        var store = await Store();
        var command = Open();
        var target = Target(command);
        await store.ExecuteStoreLifecycleAsync("branch", command);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadStoreLifecycleRequestAsync("branch", "foreign-device", target));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadStoreLifecycleRequestAsync("branch", "device", target with { CashAmount = 600 }));
        Assert.Equal("NotFound", (await store.ReadStoreLifecycleRequestAsync("other", "device", target)).Status);
        var pending = target with { RequestId = "pending" };
        await store.RetireStoreLifecycleRequestAsync("branch", "device", pending, Actor());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadStoreLifecycleRequestAsync("branch", "device", pending with { ShiftId = "other" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetireStoreLifecycleRequestAsync("branch", "foreign-device", pending, Actor(device: "foreign-device")));
    }

    [Fact]
    public async Task Retirement_and_execution_race_resolves_to_one_durable_outcome()
    {
        var store = await Store();
        var command = Open();
        var target = Target(command);
        var execution = Task.Run(async () =>
        {
            try { return await store.ExecuteStoreLifecycleAsync("branch", command); }
            catch (InvalidOperationException) { return null; }
        });
        var retirement = Task.Run(() => store.RetireStoreLifecycleRequestAsync("branch", "device", target, Actor()));
        await Task.WhenAll(execution, retirement);
        var status = await store.ReadStoreLifecycleRequestAsync("branch", "device", target);
        Assert.Equal(await retirement, status);
        if (status.Status == "Completed")
        {
            Assert.NotNull(await execution);
            Assert.Equal(await execution, status.Result);
        }
        else
        {
            Assert.Equal("Retired", status.Status);
            Assert.Null(await execution);
            Assert.Null(await store.ReadCurrentStoreLifecycleAsync("branch"));
        }
    }

    [Fact]
    public async Task Completed_close_remains_immutable_when_retirement_is_requested()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Open());
        var close = Open() with { RequestId = "close", Action = StoreLifecycleAction.CloseStore, CashAmount = 490 };
        var saved = await store.ExecuteStoreLifecycleAsync("branch", close);
        var result = await store.RetireStoreLifecycleRequestAsync("branch", "device", Target(close), Actor("other"));
        Assert.Equal("Completed", result.Status);
        Assert.Equal(saved, result.Result);
        Assert.Equal(-10, result.Result!.Closing!.CashDifference);
        Assert.Equal(saved, await store.ReadCurrentStoreLifecycleAsync("branch"));
    }

    [Fact]
    public async Task Migration_from30_keeps_completed_commands()
    {
        var store = await Store();
        var saved = await store.ExecuteStoreLifecycleAsync("branch", Open());
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var old = connection.CreateCommand();
        old.CommandText = "DROP TABLE store_lifecycle_retirements; PRAGMA user_version=30;";
        await old.ExecuteNonQueryAsync();
        store = await Store();
        Assert.Equal(saved, (await store.ReadStoreLifecycleRequestAsync("branch", "device", Target(Open()))).Result);
        old.CommandText = "PRAGMA user_version;";
        Assert.Equal(32L, await old.ExecuteScalarAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
