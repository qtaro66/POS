using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteStoreTableSummaryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"store-tables-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 20);
    private static StoreLifecycleCommand Command(string request, StoreLifecycleAction action, string shift = "first") =>
        new(request, "store", shift, action, action is StoreLifecycleAction.OpenStore or StoreLifecycleAction.CloseStore ? 0 : null,
            new("employee", "device", Now, Day));

    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }

    private async Task Execute(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Live_counts_include_open_and_payment_states_and_isolate_the_branch()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore));
        await Execute("""
            INSERT INTO table_sessions VALUES
            ('branch','ordering','A01','Ordering',1),
            ('branch','pending','A02','PaymentPending',1),
            ('branch','progress','A03','PaymentInProgress',1),
            ('branch','partial','A04','PartiallyPaid',1),
            ('branch','completed','A05','Completed',1),
            ('branch','closed','A06','Closed',1),
            ('branch','cancelled','A07','Cancelled',1),
            ('other','foreign','B01','PartiallyPaid',1);
            """);
        var live = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(new StoreTableTotals(4, 3, 1), live.Tables);
        Assert.Equal(live.Tables, live.CashSummary!.Tables);
        Assert.Equal(new StoreTableTotals(1, 1, 1),
            (await store.ReadStoreLifecycleWorkspaceAsync("other", Day, Now)).Tables);
        var blocked = await Assert.ThrowsAsync<StoreClosingBlockedException>(() =>
            store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseStore)));
        Assert.Equal(new StoreClosingBlockedTable("partial", "A04"), Assert.Single(blocked.Tables));
        Assert.Equal("first", (await store.ReadCurrentStoreLifecycleAsync("branch"))!.ActiveShiftId);
        Assert.Null(await store.ReadStoreShiftClosingAsync("branch", "store", "first"));
    }

    [Fact]
    public async Task Closing_freezes_table_counts_and_replay_does_not_recount_after_handover()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore));
        await Execute("""
            INSERT INTO table_sessions VALUES
            ('branch','one','A01','Ordering',1), ('branch','two','A02','PaymentPending',1);
            """);
        var close = Command("close", StoreLifecycleAction.CloseShift);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        var saved = await store.ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.Equal(new StoreTableTotals(2, 1, 0), saved!.CashSummary!.Tables);
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, "second"));
        await Execute("UPDATE table_sessions SET status='Completed';");
        Assert.Equal(new StoreTableTotals(0, 0, 0),
            (await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now)).Tables);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        Assert.Equal(saved, await (await Store()).ReadStoreShiftClosingAsync("branch", "store", "first"));
    }

    [Fact]
    public async Task Earlier_snapshots_without_table_counts_remain_unavailable()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore));
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore));
        await Execute("UPDATE store_lifecycle_cash_snapshots SET summary_json=json_remove(summary_json,'$.Tables');");
        var saved = await (await Store()).ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.NotNull(saved!.Closing);
        Assert.Null(saved.CashSummary!.Tables);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
