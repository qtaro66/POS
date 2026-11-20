using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteClosingReportOutboxTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"closing-outbox-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseTime = TimeSpan.FromMinutes(1);
    private static StoreLifecycleCommand Command(string request, StoreLifecycleAction action, decimal? cash = null,
        string shift = "first") => new(request, "store", shift, action, cash,
            new("employee", "device", Now, new(2026, 11, 20)));
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }
    private async Task Sql(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private async Task<SqliteTableSessionCommandStore> Closed(string branch = "branch")
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync(branch, Command("open", StoreLifecycleAction.OpenStore, 500));
        await store.ExecuteStoreLifecycleAsync(branch, Command("close", StoreLifecycleAction.CloseStore, 490));
        return store;
    }

    [Fact]
    public async Task Only_closings_enqueue_and_replay_preserves_original_snapshot()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        Assert.Null(await store.ClaimClosingReportAsync("branch", "worker", Now, LeaseTime));
        var close = Command("close", StoreLifecycleAction.CloseShift);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        await store.ExecuteStoreLifecycleAsync("branch", close);
        var first = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "worker", Now, LeaseTime));
        using var document = JsonDocument.Parse(first.PayloadJson);
        var payload = document.RootElement;
        Assert.Equal("CloseShift", payload.GetProperty("operation").GetString());
        Assert.Equal(JsonValueKind.Object, payload.GetProperty("closing").ValueKind);
        Assert.Equal(500, payload.GetProperty("closing").GetProperty("ExpectedCashAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("closing").GetProperty("ActualCashAmount").ValueKind);
        Assert.Equal("CashSales", payload.GetProperty("cashSummary").GetProperty("Source").GetString());
        await store.ExecuteStoreLifecycleAsync("branch", Command("next", StoreLifecycleAction.OpenShift, shift: "second"));
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", Now)).PendingCount);
        await store.ExecuteStoreLifecycleAsync("branch", Command("end", StoreLifecycleAction.CloseStore, 490, "second"));
        var second = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "worker2", Now, LeaseTime));
        Assert.Equal("CloseStore", second.Operation);
        Assert.NotEqual(first.ReportId, second.ReportId);
        var reopened = await Store();
        var recovered = Assert.IsType<ClosingReportLease>(await reopened.ClaimClosingReportAsync("branch", "worker3", Now.AddMinutes(1), LeaseTime));
        Assert.Equal(first.PayloadJson, recovered.PayloadJson);
        Assert.Equal(first.ReportId, recovered.ReportId);
    }

    [Fact]
    public async Task Lease_expiry_fences_stale_workers_and_confirmation_is_once_only()
    {
        var store = await Closed();
        var first = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "one", Now, LeaseTime));
        Assert.Null(await store.ClaimClosingReportAsync("branch", "two", Now.AddSeconds(59), LeaseTime));
        Assert.False(await store.ConfirmClosingReportAsync(first with { LeaseOwner = "other" }, Now));
        Assert.False(await store.ConfirmClosingReportAsync(first with { BranchId = "other" }, Now));
        var second = Assert.IsType<ClosingReportLease>(await (await Store()).ClaimClosingReportAsync("branch", "two", Now.AddMinutes(1), LeaseTime));
        Assert.Equal(2, second.AttemptCount);
        Assert.NotEqual(first.LeaseId, second.LeaseId);
        Assert.Equal(first.ReportId, second.ReportId);
        Assert.Equal(first.PayloadJson, second.PayloadJson);
        Assert.False(await store.ConfirmClosingReportAsync(first, Now.AddMinutes(1)));
        Assert.False(await store.RetryClosingReportAsync(first, Now.AddMinutes(1), LeaseTime, ClosingReportDeliveryFailure.TransportUnavailable));
        Assert.True(await store.ConfirmClosingReportAsync(second, Now.AddMinutes(1)));
        Assert.False(await store.ConfirmClosingReportAsync(second, Now.AddMinutes(1)));
        var status = await store.ReadClosingReportOutboxAsync("branch", Now.AddMinutes(1));
        Assert.Equal(0, status.PendingCount);
        Assert.Equal(0, status.LeasedCount);
        Assert.Equal(1, status.DeliveredCount);
        Assert.Equal(Now.AddMinutes(1), status.LastDeliveredAt);
    }

    [Fact]
    public async Task Retry_delay_and_branch_boundaries_preserve_payload()
    {
        var store = await Closed();
        await Closed("other");
        var first = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "one", Now, LeaseTime));
        Assert.True(await store.RetryClosingReportAsync(first, Now, LeaseTime, ClosingReportDeliveryFailure.AcknowledgmentUnavailable));
        Assert.Null(await store.ClaimClosingReportAsync("branch", "one", Now.AddSeconds(59), LeaseTime));
        Assert.NotNull(await store.ClaimClosingReportAsync("other", "one", Now, LeaseTime));
        var retry = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "one", Now.AddMinutes(1), LeaseTime));
        Assert.Equal(first.PayloadJson, retry.PayloadJson);
        Assert.Equal(first.ReportId, retry.ReportId);
        Assert.Equal(2, retry.AttemptCount);
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("other", Now)).PendingCount);
        Assert.Equal(0, (await store.ReadClosingReportOutboxAsync("missing", Now)).PendingCount);
    }

    [Fact]
    public async Task Concurrent_workers_cannot_claim_the_same_report()
    {
        var store = await Closed();
        var other = await Store();
        var claims = await Task.WhenAll(
            store.ClaimClosingReportAsync("branch", "one", Now, LeaseTime),
            other.ClaimClosingReportAsync("branch", "two", Now, LeaseTime));
        Assert.Single(claims, claim => claim is not null);
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", Now)).LeasedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version_32_backfill_is_idempotent_and_never_recalculates_missing_snapshots(bool missingSnapshot)
    {
        await Closed();
        await Sql("DROP TABLE closing_report_outbox; PRAGMA user_version=32;");
        if (missingSnapshot) { await Sql("DELETE FROM store_lifecycle_cash_snapshots;"); }
        var store = await Store();
        await Store();
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", Now)).PendingCount);
        var lease = Assert.IsType<ClosingReportLease>(await store.ClaimClosingReportAsync("branch", "worker", Now, LeaseTime));
        using var payload = JsonDocument.Parse(lease.PayloadJson);
        Assert.Equal(missingSnapshot ? JsonValueKind.Null : JsonValueKind.Object,
            payload.RootElement.GetProperty("cashSummary").ValueKind);
        Assert.Equal(490, payload.RootElement.GetProperty("closing").GetProperty("ActualCashAmount").GetDecimal());
        Assert.Equal(-10, payload.RootElement.GetProperty("closing").GetProperty("CashDifference").GetDecimal());
    }

    [Fact]
    public async Task Failed_enqueue_rolls_back_close_and_snapshot_then_allows_retry()
    {
        var store = await Store();
        await store.ExecuteStoreLifecycleAsync("branch", Command("open", StoreLifecycleAction.OpenStore, 500));
        await Sql("CREATE TRIGGER fail_report BEFORE INSERT ON closing_report_outbox BEGIN SELECT RAISE(ABORT,'outbox unavailable'); END;");
        var close = Command("close", StoreLifecycleAction.CloseStore, 500);
        await Assert.ThrowsAsync<SqliteException>(() => store.ExecuteStoreLifecycleAsync("branch", close));
        var workspace = await store.ReadStoreLifecycleWorkspaceAsync("branch", new(2026, 11, 20), Now);
        Assert.NotNull(workspace.Current);
        Assert.False(workspace.Current.IsClosed);
        Assert.Equal("first", workspace.Current.ActiveShiftId);
        Assert.Null((await store.ReadStoreShiftClosingAsync("branch", "store", "first"))!.Closing);
        Assert.Equal(0, (await store.ReadClosingReportOutboxAsync("branch", Now)).PendingCount);
        await Sql("DROP TRIGGER fail_report;");
        await store.ExecuteStoreLifecycleAsync("branch", close);
        Assert.Equal(1, (await store.ReadClosingReportOutboxAsync("branch", Now)).PendingCount);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
