using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteStoreLifecycleTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"store-lifecycle-{Guid.NewGuid():N}.db");

    private static StoreLifecycleCommand Command(string request, StoreLifecycleAction action,
        decimal? cash = null, string shift = "shift", string store = "store") =>
        new(request, store, shift, action, cash,
            new("employee", "device", new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero), new(2026, 11, 19)));

    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public async Task Reopening_database_preserves_state_and_replay_returns_original_result()
    {
        var store = await Store();
        var open = Command("open", StoreLifecycleAction.OpenStore, 500);
        var original = await store.ExecuteStoreLifecycleAsync("branch", open);
        var restarted = await Store();
        Assert.Equal(original, await restarted.ReadStoreLifecycleAsync("branch", "store"));
        await restarted.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift));
        Assert.Equal(original, await restarted.ExecuteStoreLifecycleAsync("branch", open));
        var next = await restarted.ExecuteStoreLifecycleAsync("branch",
            Command("next", StoreLifecycleAction.OpenShift, 999, "second"));
        Assert.Equal("second", next.ActiveShiftId);
        var closed = await restarted.ExecuteStoreLifecycleAsync("branch",
            Command("end", StoreLifecycleAction.CloseStore, 490, "second"));
        Assert.Equal(500, closed.Closing!.ExpectedCashAmount);
        Assert.Equal(-10, closed.Closing.CashDifference);
        Assert.Equal(closed, await (await Store()).ReadStoreLifecycleAsync("branch", "store"));
    }

    [Fact]
    public async Task Changed_request_payload_conflicts_and_branches_are_isolated()
    {
        var store = await Store();
        var open = Command("open", StoreLifecycleAction.OpenStore, 500);
        await store.ExecuteStoreLifecycleAsync("a", open);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ExecuteStoreLifecycleAsync("a", open with { CashAmount = 600 }));
        await store.ExecuteStoreLifecycleAsync("b", open with { CashAmount = 700 });
        Assert.Equal(500, (await store.ReadStoreLifecycleAsync("a", "store"))!.OpeningCashAmount);
        Assert.Equal(700, (await store.ReadStoreLifecycleAsync("b", "store"))!.OpeningCashAmount);
    }

    [Fact]
    public async Task Competing_opens_leave_one_active_store_and_failed_insert_rolls_back()
    {
        var store = await Store();
        var tasks = Enumerable.Range(0, 2).Select(async index =>
        {
            try
            {
                await store.ExecuteStoreLifecycleAsync("branch",
                    Command($"open-{index}", StoreLifecycleAction.OpenStore, 500, store: $"store-{index}"));
                return true;
            }
            catch (SqliteException) { return false; }
        });
        Assert.Single(await Task.WhenAll(tasks), success => success);
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT count(*) FROM store_lifecycle_commands";
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Missing_count_stale_shift_and_partial_payment_leave_active_shift_intact()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ExecuteStoreLifecycleAsync("branch", Command("stale", StoreLifecycleAction.CloseStore, 500, "stale")));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO table_sessions(branch_id,table_session_id,table_id,status,version)
                VALUES ('branch','partial','table','PartiallyPaid',1);
            """;
        await seed.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<StoreClosingBlockedException>(() =>
            store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore, 500)));
        Assert.Equal("shift", (await store.ReadStoreLifecycleAsync("branch", "store"))!.ActiveShiftId);
    }

    [Fact]
    public async Task Applied_cash_is_scoped_to_store_and_survives_shift_handover()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO table_sessions(branch_id,table_session_id,table_id,status,version)
                VALUES ('branch','sale','table','Completed',1), ('branch','other','other','Completed',1);
            INSERT INTO cash_sales VALUES
                ('branch','paid','sale','{"StoreSessionId":"store","AppliedAmount":100,"TenderedAmount":150,"ChangeAmount":50}','{}'),
                ('branch','other','other','{"StoreSessionId":"other","AppliedAmount":900}','{}');
            """;
        await seed.ExecuteNonQueryAsync();
        var shift = await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift));
        Assert.Equal(600, shift.Closing!.ExpectedCashAmount);
        Assert.Equal("NotReconciled", shift.Closing.ReconciliationStatus);
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, 999, "second"));
        var closed = await store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore, 600, "second"));
        Assert.Equal(600, closed.Closing!.ExpectedCashAmount);
        Assert.Equal(0, closed.Closing.CashDifference);
    }

    [Fact]
    public async Task Concurrent_identical_closes_replay_one_result()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 0));
        var close = Command("close", StoreLifecycleAction.CloseStore, 0);
        var results = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => store.ExecuteStoreLifecycleAsync("branch", close)));
        Assert.Equal(results[0], results[1]);
        Assert.True(results[0].IsClosed);
    }

    [Fact]
    public async Task Version29_migration_preserves_existing_data_and_is_repeatable()
    {
        await Store();
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var old = connection.CreateCommand();
        old.CommandText = """
            DROP TABLE store_lifecycle_cash_snapshots;
            DROP TABLE store_lifecycle_retirements;
            DROP TABLE store_lifecycle_commands;
            DROP TABLE store_lifecycle_sessions;
            INSERT INTO table_sessions VALUES ('existing','sale','table','Completed',1);
            PRAGMA user_version = 29;
            """;
        await old.ExecuteNonQueryAsync();
        var store = await Store();
        await store.InitializeAsync();
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT count(*) FROM table_sessions WHERE branch_id='existing';";
        Assert.Equal(1L, await check.ExecuteScalarAsync());
        check.CommandText = "PRAGMA user_version;";
        Assert.Equal(35L, await check.ExecuteScalarAsync());
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 0));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
