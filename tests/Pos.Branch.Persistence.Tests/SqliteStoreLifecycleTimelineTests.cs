using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteStoreLifecycleTimelineTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lifecycle-timeline-{Guid.NewGuid():N}.db");
    private static readonly DateOnly Day = new(2026, 11, 20);
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private static StoreLifecycleCommand Command(string id, string store, string shift, StoreLifecycleAction action,
        decimal? amount = null, int minute = 0, int day = 20) =>
        new(id, store, shift, action, amount, new("employee-" + id, "device",
            Now.AddDays(day - 20).AddMinutes(minute), new DateOnly(2026, 11, day)));
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public async Task Workspace_lists_all_same_day_sessions_and_shifts_in_committed_order()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", "first", "a", StoreLifecycleAction.OpenStore, 500));
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", "first", "a", StoreLifecycleAction.CloseShift, minute: 1));
        await store.ExecuteStoreLifecycleAsync("branch", Command("shift", "first", "b", StoreLifecycleAction.OpenShift, minute: 2));
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", "first", "b", StoreLifecycleAction.CloseStore, 490, 3));
        await store.ExecuteStoreLifecycleAsync("branch", Command("again", "second", "c", StoreLifecycleAction.OpenStore, 0, 4));
        await store.RetireStoreLifecycleRequestAsync("branch", "device",
            new("retired", "second", "c", StoreLifecycleAction.CloseStore, 0),
            new("retirer", "device", Now, Day));
        var result = await (await Store()).ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(new[] { "first", "second" }, result.Stores.Select(s => s.StoreSessionId));
        Assert.Equal(new[] { "a", "b" }, result.Stores[0].Shifts.Select(s => s.ShiftId));
        Assert.Equal("employee-open", result.Stores[0].Opened.EmployeeId);
        Assert.Equal("employee-shift", result.Stores[0].Shifts[1].Opened.EmployeeId);
        Assert.Equal("NotReconciled", result.Stores[0].Shifts[0].Closing!.ReconciliationStatus);
        Assert.Equal(-10, result.Stores[0].Shifts[1].Closing!.CashDifference);
        Assert.Null(result.Stores[1].Shifts[0].Closing);
        Assert.Equal("c", result.Current!.ActiveShiftId);
        Assert.Equal(Now, result.AsOf);
        Assert.Empty((await store.ReadStoreLifecycleWorkspaceAsync("other", Day, Now)).Stores);
    }

    [Fact]
    public async Task Earlier_closing_read_is_immutable_after_later_commands_and_scoped_by_store_and_branch()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", "first", "a", StoreLifecycleAction.OpenStore, 500));
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", "first", "a", StoreLifecycleAction.CloseShift, 510, 1));
        var saved = await store.ReadStoreShiftClosingAsync("branch", "first", "a");
        Assert.NotNull(saved);
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", "first", "b", StoreLifecycleAction.OpenShift, 999, 2));
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", "first", "b", StoreLifecycleAction.CloseStore, 490, 3));
        await store.ExecuteStoreLifecycleAsync("branch", Command("again", "second", "a", StoreLifecycleAction.OpenStore, 0, 4));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var later = connection.CreateCommand();
        later.CommandText = """
            INSERT INTO table_sessions VALUES ('branch','later','table','Completed',1);
            INSERT INTO cash_sales VALUES ('branch','later','later',
                '{"StoreSessionId":"first","AppliedAmount":900}','{}');
            """;
        await later.ExecuteNonQueryAsync();
        Assert.Equal(saved, await (await Store()).ReadStoreShiftClosingAsync("branch", "first", "a"));
        Assert.Equal(10, saved.Closing!.CashDifference);
        Assert.Null(await store.ReadStoreShiftClosingAsync("branch", "second", "a"));
        Assert.Null(await store.ReadStoreShiftClosingAsync("other", "first", "a"));
        Assert.Null(await store.ReadStoreShiftClosingAsync("branch", "missing", "a"));
    }

    [Fact]
    public async Task Current_date_timeline_excludes_prior_day_but_current_state_can_show_latest_prior_store()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", "prior", "a", StoreLifecycleAction.OpenStore, 0, day: 19));
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", "prior", "a", StoreLifecycleAction.CloseStore, 0, day: 19));
        var current = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Empty(current.Stores);
        Assert.Equal(new DateOnly(2026, 11, 19), current.Current!.BusinessDate);
        Assert.True(current.Current.IsClosed);
        Assert.Single((await store.ReadStoreLifecycleWorkspaceAsync("branch", new(2026, 11, 19), Now)).Stores);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
