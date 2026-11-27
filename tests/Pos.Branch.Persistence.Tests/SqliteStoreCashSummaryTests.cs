using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteStoreCashSummaryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"store-summary-{Guid.NewGuid():N}.db");
    private static readonly DateOnly Day = new(2026, 11, 20);
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private static StoreLifecycleCommand Command(string request, StoreLifecycleAction action, decimal? cash = null,
        string shift = "first", string store = "store") =>
        new(request, store, shift, action, cash, new("employee", "device", Now, Day));
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }

    private async Task Sale(string request, string branch, string store, string shift, decimal applied, decimal tendered, string bill = "{}")
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO table_sessions VALUES ($branch,$request,$request,'Completed',1);
            INSERT INTO cash_sales VALUES ($branch,$request,$request,$result,$bill);
            """;
        seed.Parameters.AddWithValue("$bill", bill);
        seed.Parameters.AddWithValue("$branch", branch);
        seed.Parameters.AddWithValue("$request", request);
        seed.Parameters.AddWithValue("$result", JsonSerializer.Serialize(new CashSaleResult(request, "employee", "device", 1,
            shift, store, Day.ToString("yyyy-MM-dd"), tendered, applied, tendered - applied, request, "QueuedForSimulation")));
        await seed.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Live_totals_use_decimal_applied_cash_and_scope_by_store_shift_and_branch()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await Sale("one", "branch", "store", "first", 0.1m, 1);
        await Sale("two", "branch", "store", "first", 0.2m, 1);
        await Sale("foreign-store", "branch", "other", "first", 900, 900);
        await Sale("foreign-branch", "other", "store", "first", 800, 800);
        var workspace = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal("CashSales", workspace.CashSummary!.Source);
        Assert.Equal(new StoreCashTotals(2, 0.3m), workspace.CashSummary.Store);
        Assert.Equal(workspace.CashSummary.Store, workspace.CashSummary.Shift);
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift));
        var between = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(new StoreCashTotals(2, 0.3m), between.CashSummary!.Store);
        Assert.Null(between.CashSummary.Shift);
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, shift: "second"));
        await Sale("next-sale", "branch", "store", "second", 100, 150);
        var next = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(new StoreCashTotals(3, 100.3m), next.CashSummary!.Store);
        Assert.Equal(new StoreCashTotals(1, 100), next.CashSummary.Shift);
        Assert.Null((await store.ReadStoreLifecycleWorkspaceAsync("other", Day, Now)).CashSummary);
    }

    [Fact]
    public async Task Closing_summary_is_atomic_immutable_and_replay_preserves_captured_cash()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await Sale("sale", "branch", "store", "first", 100, 150);
        var close = Command("close", StoreLifecycleAction.CloseShift);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        var saved = await store.ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.Equal(new StoreCashTotals(1, 100), saved!.CashSummary!.Shift);
        Assert.Equal(600, saved.Closing!.ExpectedCashAmount);
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, shift: "second"));
        await Sale("later", "branch", "store", "first", 900, 900);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        var restored = await (await Store()).ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.Equal(saved, restored);
        var end = Command("end", StoreLifecycleAction.CloseStore, 1500, "second");
        await store.ExecuteStoreLifecycleAsync("branch", end);
        var closingStore = await store.ReadStoreShiftClosingAsync("branch", "store", "second");
        Assert.Equal(new StoreCashTotals(2, 1000), closingStore!.CashSummary!.Store);
        Assert.Equal(new StoreCashTotals(0, 0), closingStore.CashSummary.Shift);
        Assert.Null((await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now)).CashSummary);
        Assert.Null(await store.ReadStoreShiftClosingAsync("other", "store", "first"));
    }

    [Fact]
    public async Task Failed_snapshot_write_rolls_back_the_closing_command()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 0));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var fail = connection.CreateCommand();
        fail.CommandText = """
            CREATE TRIGGER fail_snapshot BEFORE INSERT ON store_lifecycle_cash_snapshots
            BEGIN SELECT RAISE(ABORT,'snapshot unavailable'); END;
            """;
        await fail.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<SqliteException>(() =>
            store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseStore, 0)));
        Assert.Equal("first", (await store.ReadCurrentStoreLifecycleAsync("branch"))!.ActiveShiftId);
        Assert.Null(await store.ReadStoreShiftClosingAsync("branch", "store", "first"));
        fail.CommandText = "SELECT count(*) FROM store_lifecycle_commands;";
        Assert.Equal(1L, await fail.ExecuteScalarAsync());
        fail.CommandText = "DROP TRIGGER fail_snapshot;";
        await fail.ExecuteNonQueryAsync();
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseStore, 0));
        Assert.NotNull((await store.ReadStoreShiftClosingAsync("branch", "store", "first"))!.CashSummary);
    }

    [Fact]
    public async Task Version31_closing_remains_unavailable_after_migration_instead_of_backfilling()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 0));
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseStore, 0));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var old = connection.CreateCommand();
        old.CommandText = "DROP TABLE store_lifecycle_cash_snapshots; PRAGMA user_version=31;";
        await old.ExecuteNonQueryAsync();
        store = await Store();
        var saved = await store.ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.NotNull(saved!.Closing);
        Assert.Null(saved.CashSummary);
        Assert.Null((await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now)).Stores[0].Shifts[0].CashSummary);
        old.CommandText = "PRAGMA user_version;";
        Assert.Equal(36L, await old.ExecuteScalarAsync());
    }

    private const string CapturedBill = """
        {"itemGrossAmount":120,"lineDiscountAmount":10,"billDiscount":{"appliedAmount":5},
         "serviceChargeAmount":5.9,"totalBeforeRounding":110.9,"roundingAdjustment":-0.9,
         "netTotal":110,"vatInclusiveAmount":7.26,"amountBeforeVat":103.64}
        """;

    [Fact]
    public async Task Captured_financials_preserve_discount_tax_and_rounding_across_shifts_and_replay()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await Sale("one", "branch", "store", "first", 110, 120, CapturedBill);
        var totals = new StoreBillTotals(120, 10, 5, 5.9m, 110.9m, -0.9m, 110, 7.26m, 103.64m);
        var live = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(totals, live.CashSummary!.Shift!.Financials);
        var close = Command("close", StoreLifecycleAction.CloseShift);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        var original = await store.ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.Equal(totals, original!.CashSummary!.Shift!.Financials);
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, shift: "second"));
        await Sale("two", "branch", "store", "second", 110, 200, CapturedBill);
        var later = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(totals.Add(totals), later.CashSummary!.Store.Financials);
        Assert.Equal(totals, later.CashSummary.Shift!.Financials);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        Assert.Equal(original, await (await Store()).ReadStoreShiftClosingAsync("branch", "store", "first"));
    }

    [Fact]
    public async Task Incomplete_or_inconsistent_bills_do_not_produce_partial_financial_totals()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 0));
        await Sale("complete", "branch", "store", "first", 110, 120, CapturedBill);
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseShift));
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, shift: "second"));
        await Sale("older", "branch", "store", "second", 10, 10);
        var mixed = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(120, mixed.CashSummary!.Store.AppliedCashAmount);
        Assert.Null(mixed.CashSummary.Store.Financials);
        Assert.Null(mixed.CashSummary.Shift!.Financials);
        await Sale("mismatch", "branch", "store", "second", 100, 120, CapturedBill);
        var mismatched = await store.ReadStoreLifecycleWorkspaceAsync("branch", Day, Now);
        Assert.Equal(220, mismatched.CashSummary!.Store.AppliedCashAmount);
        Assert.Null(mismatched.CashSummary.Store.Financials);
        var saved = await store.ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.NotNull(saved!.CashSummary!.Shift!.Financials);
    }
    [Fact]
    public async Task Legacy_cash_snapshot_keeps_missing_financials_even_when_bill_data_is_available()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 0));
        await Sale("one", "branch", "store", "first", 110, 120, CapturedBill);
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseStore, 110));
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var legacy = connection.CreateCommand();
        legacy.CommandText = """
            UPDATE store_lifecycle_cash_snapshots
            SET summary_json = json_remove(summary_json, '$.Store.Financials', '$.Shift.Financials');
            """;
        await legacy.ExecuteNonQueryAsync();
        var saved = await (await Store()).ReadStoreShiftClosingAsync("branch", "store", "first");
        Assert.Equal(110, saved!.CashSummary!.Store.AppliedCashAmount);
        Assert.Null(saved.CashSummary.Store.Financials);
        Assert.Null(saved.CashSummary.Shift!.Financials);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
