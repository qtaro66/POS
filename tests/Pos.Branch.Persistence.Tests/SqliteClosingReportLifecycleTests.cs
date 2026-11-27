using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteClosingReportLifecycleTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"closing-context-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);
    private static StoreLifecycleCommand Command(string request, StoreLifecycleAction action, decimal? amount = null,
        string shift = "first", string store = "store", string employee = "employee") => new(request, store, shift, action, amount,
            new(employee, "device", Now, new(2026, 11, 21)));
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }
    private async Task Sql(string text, params (string Name, object? Value)[] args)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Foreign Keys=True");
        await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = text;
        foreach (var (name, value) in args) { command.Parameters.AddWithValue(name, value ?? DBNull.Value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Scalar(string text)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = text;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
    private async Task<SqliteTableSessionCommandStore> Close()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseStore, 490));
        return store;
    }

    [Fact]
    public async Task Multi_shift_context_freezes_opening_attribution_and_uncounted_shifts_without_summing_optional_floats()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500, employee: "opener"));
        await store.ExecuteStoreLifecycleAsync("branch", Command("one", StoreLifecycleAction.CloseShift, employee: "first-closer"));
        var first = Assert.IsType<ClosingReportLifecycleContext>((await store.ReadClosingReportDetailAsync("branch", "one", Now))!.Lifecycle);
        Assert.Single(first.Shifts);
        Assert.False(first.ClosesStore);
        Assert.Null(first.Shifts[0].Closing.ActualCashAmount);
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, 200, "second", employee: "second-opener"));
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore, 490, "second", employee: "store-closer"));
        var end = Assert.IsType<ClosingReportLifecycleContext>((await store.ReadClosingReportDetailAsync("branch", "end", Now))!.Lifecycle);
        Assert.Equal("opener", end.Opened.EmployeeId);
        Assert.Equal(500, end.OpeningCashAmount);
        Assert.Equal(new[] { "first", "second" }, end.Shifts.Select(shift => shift.ShiftId));
        Assert.Equal(200, end.Shifts[1].OpeningCashAmount);
        Assert.Equal("second-opener", end.Shifts[1].Opened.EmployeeId);
        Assert.Equal("store-closer", end.Shifts[1].Closing.Context.EmployeeId);
        Assert.Equal(500, end.Shifts[1].Closing.ExpectedCashAmount);
        Assert.Equal(-10, end.Shifts[1].Closing.CashDifference);
        Assert.Equal("NotReconciled", end.Shifts[0].Closing.ReconciliationStatus);
        Assert.Equal("Reconciled", end.Shifts[1].Closing.ReconciliationStatus);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize((await store.ReadClosingReportDetailAsync("branch", "one", Now))!.Lifecycle));
    }

    [Fact]
    public async Task Replay_reopen_later_store_and_foreign_branch_do_not_change_frozen_context_or_payload()
    {
        var store = await Close();
        var before = await Scalar("SELECT context_json FROM closing_report_lifecycle_snapshots;");
        var payload = await Scalar("SELECT payload_json FROM closing_report_outbox;");
        await store.ExecuteStoreLifecycleAsync("branch", Command("close", StoreLifecycleAction.CloseStore, 490));
        await store.ExecuteStoreLifecycleAsync("branch", Command("another", StoreLifecycleAction.OpenStore, 900, store: "later"));
        await store.ExecuteStoreLifecycleAsync("foreign", Command("open", StoreLifecycleAction.OpenStore, 100));
        var reopened = await Store();
        Assert.Equal(before, await Scalar("SELECT context_json FROM closing_report_lifecycle_snapshots;"));
        Assert.Equal(payload, await Scalar("SELECT payload_json FROM closing_report_outbox;"));
        Assert.Equal("1", await Scalar("SELECT count(*) FROM closing_report_lifecycle_snapshots;"));
        Assert.NotNull((await reopened.ReadClosingReportDetailAsync("branch", "close", Now))!.Lifecycle);
        Assert.Null(await reopened.ReadClosingReportDetailAsync("foreign", "close", Now));
    }

    [Fact]
    public async Task Version_33_migration_preserves_report_bytes_and_acknowledgement_without_backfilling_context()
    {
        var store = await Close();
        var lease = (await store.ClaimClosingReportAsync("branch", "worker", Now, TimeSpan.FromMinutes(1)))!;
        Assert.True(await store.ConfirmClosingReportAsync(lease, Now));
        var payload = await Scalar("SELECT payload_json FROM closing_report_outbox;");
        await Sql(global::Pos.Branch.Persistence.Tests.LegacySchemaFixture.WithoutKitchenReprints("DROP TABLE closing_report_context_outbox; DROP TABLE closing_report_lifecycle_snapshots; PRAGMA user_version=33;"));
        var migrated = await Store();
        Assert.Equal("36", await Scalar("PRAGMA user_version;"));
        Assert.Equal(payload, await Scalar("SELECT payload_json FROM closing_report_outbox;"));
        Assert.Equal(1, (await migrated.ReadClosingReportOutboxAsync("branch", Now)).DeliveredCount);
        Assert.Null((await migrated.ReadClosingReportDetailAsync("branch", "close", Now))!.Lifecycle);
        await migrated.ExecuteStoreLifecycleAsync("branch", Command("new-open", StoreLifecycleAction.OpenStore, 100, store: "new-store"));
        await migrated.ExecuteStoreLifecycleAsync("branch", Command("new-close", StoreLifecycleAction.CloseStore, 100, store: "new-store"));
        Assert.NotNull((await migrated.ReadClosingReportDetailAsync("branch", "new-close", Now))!.Lifecycle);
    }

    [Fact]
    public async Task Companion_failure_rolls_back_lifecycle_cash_snapshot_and_outbox_then_same_request_can_retry()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await Sql("CREATE TRIGGER fail_context BEFORE INSERT ON closing_report_lifecycle_snapshots BEGIN SELECT RAISE(ABORT,'context unavailable'); END;");
        var close = Command("close", StoreLifecycleAction.CloseStore, 500);
        await Assert.ThrowsAsync<SqliteException>(() => store.ExecuteStoreLifecycleAsync("branch", close));
        Assert.Equal("0", await Scalar("SELECT count(*) FROM closing_report_outbox;"));
        Assert.Equal("0", await Scalar("SELECT count(*) FROM store_lifecycle_cash_snapshots;"));
        Assert.Equal("1", await Scalar("SELECT count(*) FROM store_lifecycle_commands;"));
        Assert.False((await store.ReadCurrentStoreLifecycleAsync("branch"))!.IsClosed);
        await Sql("DROP TRIGGER fail_context;");
        await store.ExecuteStoreLifecycleAsync("branch", close);
        Assert.NotNull((await store.ReadClosingReportDetailAsync("branch", "close", Now))!.Lifecycle);
    }

    [Theory]
    [InlineData("BranchId", "\"foreign\"")]
    [InlineData("RequestId", "\"other\"")]
    [InlineData("StoreSessionId", "\"other\"")]
    [InlineData("ClosingShiftId", "\"other\"")]
    [InlineData("SchemaVersion", "2")]
    [InlineData("OpeningCashAmount", "-1")]
    [InlineData("ClosesStore", "false")]
    [InlineData("Shifts", "[]")]
    public async Task Corrupt_companion_context_fails_closed_without_reconstructing_history(string name, string json)
    {
        var store = await Close();
        var root = JsonNode.Parse(await Scalar("SELECT context_json FROM closing_report_lifecycle_snapshots;"))!;
        root[name] = JsonNode.Parse(json);
        await Sql("UPDATE closing_report_lifecycle_snapshots SET context_json=$json;", ("$json", root.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadClosingReportDetailAsync("branch", "close", Now));
    }

    [Theory]
    [InlineData("Opened", "null")]
    [InlineData("Opened.BranchLocalDate", "\"2026-11-20\"")]
    [InlineData("Shifts.0.Opened", "null")]
    [InlineData("Shifts.0.Closing", "null")]
    [InlineData("Shifts.0.Closing.Context", "null")]
    [InlineData("Shifts.0.OpeningCashAmount", "900")]
    [InlineData("Shifts.0.ShiftId", "\"foreign-shift\"")]
    public async Task Corrupt_nested_attribution_or_shift_context_is_unavailable(string path, string json)
    {
        var store = await Close();
        var root = JsonNode.Parse(await Scalar("SELECT context_json FROM closing_report_lifecycle_snapshots;"))!;
        var parts = path.Split('.');
        var current = root;
        foreach (var part in parts[..^1]) { current = current is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]! : current[part]!; }
        current[parts[^1]] = JsonNode.Parse(json);
        await Sql("UPDATE closing_report_lifecycle_snapshots SET context_json=$json;", ("$json", root.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadClosingReportDetailAsync("branch", "close", Now));
    }

    [Fact]
    public async Task Large_shift_history_is_local_and_does_not_expand_the_immutable_transport_payload()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        for (var index = 0; index < 100; index++)
        {
            var shift = index == 0 ? "first" : "shift-" + index;
            if (index > 0) { await store.ExecuteStoreLifecycleAsync("branch", Command("open-" + index, StoreLifecycleAction.OpenShift, shift: shift)); }
            await store.ExecuteStoreLifecycleAsync("branch", Command("close-" + index,
                index == 99 ? StoreLifecycleAction.CloseStore : StoreLifecycleAction.CloseShift,
                index == 99 ? 500 : null, shift));
        }
        var detail = (await store.ReadClosingReportDetailAsync("branch", "close-99", Now))!;
        Assert.Equal(100, detail.Lifecycle!.Shifts.Count);
        var payload = await Scalar("SELECT payload_json FROM closing_report_outbox WHERE request_id='close-99';");
        Assert.DoesNotContain("Lifecycle", payload, StringComparison.OrdinalIgnoreCase);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload) < 16 * 1024);
        Assert.True(int.Parse(await Scalar("SELECT length(context_json) FROM closing_report_lifecycle_snapshots WHERE request_id='close-99';"), System.Globalization.CultureInfo.InvariantCulture) > payload.Length * 10);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
