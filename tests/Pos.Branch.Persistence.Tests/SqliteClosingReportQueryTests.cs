using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteClosingReportQueryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"closing-query-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private async Task<SqliteTableSessionCommandStore> Store()
    {
        var store = new SqliteTableSessionCommandStore(_path);
        await store.InitializeAsync();
        return store;
    }
    private async Task Close(string request, string branch = "branch", string storeId = "store", bool uncounted = false, DateTimeOffset? occurred = null)
    {
        var store = await Store();
        var context = new Pos.Domain.StoreSessions.StoreOperationContext("employee", "device", occurred ?? Now, new(2026, 11, 20));
        var open = new StoreLifecycleCommand("open-" + request, storeId, "shift-" + request, StoreLifecycleAction.OpenStore, 500, context);
        await store.ExecuteStoreLifecycleAsync(branch, open);
        await store.ExecuteStoreLifecycleAsync(branch, open with { RequestId = request, Action = uncounted ? StoreLifecycleAction.CloseShift : StoreLifecycleAction.CloseStore, CashAmount = uncounted ? null : 490 });
    }
    private async Task Execute(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Raw()
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json_group_array(json_object('sequence',sequence,'payload',payload_json,'lease',lease_id,'attempts',attempt_count,'available',available_at,'delivered',delivered_at,'failure',last_failure)) FROM closing_report_outbox ORDER BY sequence;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Page_is_branch_scoped_newest_first_and_tied_times_use_sequence()
    {
        await Close("one", storeId: "one");
        await Close("foreign", "other", "foreign");
        await Close("two", storeId: "two");
        var store = await Store();
        var page = await store.ReadClosingReportPageAsync("branch", Now, new());
        Assert.Equal("branch", page.BranchId);
        Assert.Equal(Now, page.AsOf);
        Assert.Equal(new[] { "two", "one" }, page.Items.Select(item => item.RequestId));
        Assert.All(page.Items, item => Assert.Equal("Queued", item.DeliveryState));
        Assert.Null(page.NextCursor);
        Assert.Empty((await store.ReadClosingReportPageAsync("missing", Now, new())).Items);
        Assert.Null(await store.ReadClosingReportDetailAsync("other", "one", Now));
        Assert.Null(await store.ReadClosingReportDetailAsync("branch", "missing", Now));
    }

    [Fact]
    public async Task Keyset_pages_exclude_later_append_without_duplicates_and_bind_all_filters()
    {
        await Close("one", storeId: "one");
        await Close("two", storeId: "two");
        await Close("three", storeId: "three");
        var store = await Store();
        var query = new ClosingReportQuery(Limit: 1);
        var first = await store.ReadClosingReportPageAsync("branch", Now, query);
        Assert.Equal("three", Assert.Single(first.Items).RequestId);
        Assert.NotNull(first.NextCursor);
        await Close("four", storeId: "four");
        var second = await store.ReadClosingReportPageAsync("branch", Now, query with { Cursor = first.NextCursor });
        Assert.Equal("two", Assert.Single(second.Items).RequestId);
        var third = await store.ReadClosingReportPageAsync("branch", Now, query with { Cursor = second.NextCursor });
        Assert.Equal("one", Assert.Single(third.Items).RequestId);
        Assert.Null(third.NextCursor);
        Assert.Equal("four", (await store.ReadClosingReportPageAsync("branch", Now, query)).Items[0].RequestId);
        foreach (var changed in new[]
        {
            query with { Status = "Pending" }, query with { Limit = 2 }, query with { StoreSessionId = "three" },
            query with { ShiftId = "shift-three" }, query with { BusinessDate = "2026-11-20" }
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, changed with { Cursor = first.NextCursor }));
        }
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("other", Now, query with { Cursor = first.NextCursor }));
    }

    [Fact]
    public async Task Filters_match_original_store_shift_date_and_delivery_state()
    {
        await Close("one", storeId: "one");
        await Close("two", storeId: "two");
        var store = await Store();
        var lease = (await store.ClaimClosingReportAsync("branch", "worker", Now, Lease))!;
        Assert.True(await store.ConfirmClosingReportAsync(lease, Now));
        var delivered = await store.ReadClosingReportPageAsync("branch", Now, new("Delivered", "one", "shift-one", "2026-11-20"));
        Assert.Equal("one", Assert.Single(delivered.Items).RequestId);
        Assert.Equal("Delivered", delivered.Items[0].DeliveryState);
        Assert.Equal(Now, delivered.Items[0].DeliveredAt);
        Assert.Equal("two", Assert.Single((await store.ReadClosingReportPageAsync("branch", Now, new("Pending"))).Items).RequestId);
        Assert.Empty((await store.ReadClosingReportPageAsync("branch", Now, new(BusinessDate: "2026-11-21"))).Items);
        Assert.Empty((await store.ReadClosingReportPageAsync("branch", Now, new(StoreSessionId: "one", ShiftId: "shift-two"))).Items);
        Assert.Empty((await store.ReadClosingReportPageAsync("branch", Now, new(StoreSessionId: "one' OR 1=1 --"))).Items);
    }

    [Fact]
    public async Task State_projection_tracks_live_expired_deferred_ready_and_confirmed_without_mutation()
    {
        await Close("one");
        var store = await Store();
        var lease = (await store.ClaimClosingReportAsync("branch", "private-worker", Now, Lease))!;
        var before = await Raw();
        var live = (await store.ReadClosingReportDetailAsync("branch", "one", Now))!;
        Assert.Equal("InFlight", live.Report.DeliveryState);
        Assert.Equal(1, live.Report.AttemptCount);
        Assert.Equal("LeaseExpired", (await store.ReadClosingReportDetailAsync("branch", "one", Now.AddMinutes(1)))!.Report.DeliveryState);
        Assert.Equal(before, await Raw());
        Assert.DoesNotContain("private-worker", JsonSerializer.Serialize(live));
        Assert.DoesNotContain(lease.LeaseId, JsonSerializer.Serialize(live));
        Assert.True(await store.RetryClosingReportAsync(lease, Now, Lease, ClosingReportDeliveryFailure.AcknowledgmentUnavailable));
        var deferred = (await store.ReadClosingReportDetailAsync("branch", "one", Now))!;
        Assert.Equal("RetryScheduled", deferred.Report.DeliveryState);
        Assert.Equal("AcknowledgmentUnavailable", deferred.Report.LastFailure);
        Assert.Equal("Queued", (await store.ReadClosingReportDetailAsync("branch", "one", Now.AddMinutes(1)))!.Report.DeliveryState);
        await Execute("UPDATE closing_report_outbox SET last_failure='secret raw exception';");
        var sanitized = (await store.ReadClosingReportDetailAsync("branch", "one", Now))!;
        Assert.Null(sanitized.Report.LastFailure);
        Assert.Equal("RetryScheduled", sanitized.Report.DeliveryState);
    }

    [Fact]
    public async Task Detail_projects_saved_financial_and_table_snapshot_after_reopen_and_legacy_null_is_unavailable()
    {
        await Close("one");
        await Execute("""
            UPDATE closing_report_outbox SET payload_json=json_set(payload_json,'$.cashSummary',json('{"Source":"CashSales","Store":{"ReceiptCount":2,"AppliedCashAmount":0.3,"Financials":{"ItemGrossAmount":0.3,"LineDiscountAmount":0,"BillDiscountAmount":0,"ServiceChargeAmount":0,"TotalBeforeRounding":0.3,"RoundingAdjustment":0,"AmountDue":0.3,"VatInclusiveAmount":0.02,"AmountBeforeVat":0.28}},"Shift":{"ReceiptCount":2,"AppliedCashAmount":0.3},"Tables":{"OpenTableCount":2,"AwaitingPaymentTableCount":1,"PartiallyPaidTableCount":0}}'));
            """);
        var store = await Store();
        var original = (await store.ReadClosingReportDetailAsync("branch", "one", Now))!;
        Assert.Equal(0.3m, original.Snapshot.CashSummary!.Store.AppliedCashAmount);
        Assert.Equal(0.02m, original.Snapshot.CashSummary.Store.Financials!.VatInclusiveAmount);
        Assert.Equal(2, original.Snapshot.CashSummary.Tables!.OpenTableCount);
        await Close("later", storeId: "later");
        var reopened = await Store();
        var saved = (await reopened.ReadClosingReportDetailAsync("branch", "one", Now))!;
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(saved));
        Assert.Equal(490, saved.Snapshot.Closing.ActualCashAmount);
        Assert.Equal(-10, saved.Snapshot.Closing.CashDifference);
        await Execute("UPDATE closing_report_outbox SET payload_json=json_set(payload_json,'$.cashSummary',null) WHERE request_id='one';");
        Assert.Null((await reopened.ReadClosingReportDetailAsync("branch", "one", Now))!.Snapshot.CashSummary);
    }

    [Fact]
    public async Task Uncounted_shift_and_fractional_source_timestamp_are_preserved()
    {
        await Close("one", uncounted: true, occurred: Now.AddTicks(1234000));
        var store = await Store();
        var detail = (await store.ReadClosingReportDetailAsync("branch", "one", Now))!;
        Assert.Equal(Now.AddTicks(1234000), detail.Report.CreatedAt);
        Assert.Null(detail.Snapshot.Closing.ActualCashAmount);
        Assert.Null(detail.Snapshot.Closing.CashDifference);
        Assert.Equal("NotReconciled", detail.Snapshot.Closing.ReconciliationStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Invalid_limits_are_rejected(int limit)
    {
        var store = await Store();
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(Limit: limit)));
    }
    [Theory]
    [InlineData("")]
    [InlineData("!")]
    [InlineData("abc")]
    [InlineData("e30")]
    public async Task Malformed_cursors_are_rejected(string cursor)
    {
        var store = await Store();
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(Cursor: cursor)));
    }
    [Fact]
    public async Task Invalid_filter_date_timezone_and_oversized_cursor_are_rejected()
    {
        var store = await Store();
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new("pending")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(BusinessDate: "2026-02-30")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(StoreSessionId: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(Cursor: new string('A', 4097))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now.ToOffset(TimeSpan.FromHours(7)), new()));
    }
    [Fact]
    public async Task Forged_duplicate_properties_and_impossible_positions_are_rejected()
    {
        await Close("one", storeId: "one");
        await Close("two", storeId: "two");
        var store = await Store();
        var page = await store.ReadClosingReportPageAsync("branch", Now, new(Limit: 1));
        var cursor = page.NextCursor!;
        var normalized = cursor.Replace('-', '+').Replace('_', '/');
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')));
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(Limit: 1, Cursor: Encode(text.Replace("\"Version\":1", "\"Version\":1,\"Version\":1")))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(Limit: 1, Cursor: Encode(text.Replace("StoreSessionId", "UnknownNullableField")))));
        var parsed = JsonNode.Parse(text)!;
        parsed["Before"] = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadClosingReportPageAsync("branch", Now, new(Limit: 1, Cursor: Encode(parsed.ToJsonString()))));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
