using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Orders;
using Pos.Application.TableSessions;
using Pos.Application.Time;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.TableSessions;
using Pos.Printing.Abstractions;

// Runs only inside the disposable BrowserTest host, before it begins accepting requests.
internal static class CashHistoryFixture
{
    public static async Task SeedAsync(string database, string branch, string employee)
    {
        const string device = "TEST-HISTORY-DEVICE";
        const string originalStore = "HISTORY-STORE-ONE";
        const string firstShift = "HISTORY-SHIFT-ONE";
        const string secondShift = "HISTORY-SHIFT-TWO";
        var clock = new FixtureClock();
        var store = new SqliteTableSessionCommandStore(database);
        var commands = new TableSessionCommandService(store, clock);
        BranchCommandMetadata Metadata(string request, string session) =>
            new(request, branch, device, employee, TableSessionId.From(session), clock.Advance());
        StoreLifecycleCommand Lifecycle(string request, string shift, StoreLifecycleAction action, decimal? cash,
            string storeId = originalStore) => new(request, storeId, shift, action, cash,
                new(employee, device, clock.Advance(), BranchDate(clock.UtcNow)));

        await store.ExecuteStoreLifecycleAsync(branch, Lifecycle("HISTORY-OPEN-STORE", firstShift, StoreLifecycleAction.OpenStore, 500));
        await Capture("ONE", "A01", [new("ITEM-1", 2, FulfillmentType.DineIn, "Original cancelled line"),
            new("ITEM-1", 1, FulfillmentType.Takeaway, "Original takeaway\nNo current-data fallback")], true);
        await store.ExecuteStoreLifecycleAsync(branch, Lifecycle("HISTORY-CLOSE-SHIFT", firstShift, StoreLifecycleAction.CloseShift, null));
        await store.ExecuteStoreLifecycleAsync(branch, Lifecycle("HISTORY-OPEN-SHIFT-TWO", secondShift, StoreLifecycleAction.OpenShift, 300));
        await Capture("TWO", "A02", [new("ITEM-1", 2, FulfillmentType.DineIn, "Original second sale")], false);
        await store.ExecuteStoreLifecycleAsync(branch, Lifecycle("HISTORY-CLOSE-STORE", secondShift, StoreLifecycleAction.CloseStore, 790));
        await store.ExecuteStoreLifecycleAsync(branch, Lifecycle("HISTORY-OPEN-CURRENT", "HISTORY-SHIFT-CURRENT",
            StoreLifecycleAction.OpenStore, 50, "HISTORY-STORE-CURRENT"));

        await Sql("""
            INSERT INTO printer_profiles (
                printer_id,branch_id,display_name,production_station_id,host,port,protocol,compatibility_profile_id,
                manufacturer,model,product_code,firmware_version,paper_width_millimeters,dpi,printable_width_dots,
                render_mode,thai_encoding_mode,status_mode,supports_cash_drawer,cutter_command_hex,drawer_pulse_command_hex,
                status_command_profile,connect_timeout_ms,send_timeout_ms,maximum_automatic_retries_before_send,
                automatic_retry_backoff_ms,is_active,updated_at)
            VALUES('HISTORY-PRINTER',$branch,'Fixture kitchen','TEST-KITCHEN','history-printer.invalid',9100,'RawTcpEscPos',
                'epson-tm-t82x-m352a-v1','Epson','TM-T82X','M352A','13.07 ESC/POS',80,203,576,
                'BitmapRaster','UnicodeRaster','Unsupported',0,'1D5601',NULL,NULL,2000,10000,30,1,1,$now);
            """);
        var dispatch = new SqlitePrintDispatchStore(database);
        var firstJob = $"{branch}:HISTORY-ORDER-ONE:production-print:1";
        for (var index = 1; index <= 22; index++)
        {
            var now = clock.Advance();
            var lease = await dispatch.ClaimNextAsync("HISTORY-PRINTER", "TEST-HISTORY-WORKER", now, TimeSpan.FromSeconds(30))
                ?? throw new InvalidOperationException("History fixture could not claim its original print job.");
            if (lease.PrintJobId != firstJob) { throw new InvalidOperationException("Unexpected history fixture print job."); }
            var attempt = "HISTORY-ATTEMPT-" + index.ToString(CultureInfo.InvariantCulture);
            await dispatch.StartAttemptAsync(lease, attempt, new string('B', 64), now);
            if (index == 22) { await dispatch.MarkFirstByteSendingAsync(lease, attempt, clock.Advance()); }
            await dispatch.CompleteAttemptAsync(lease, attempt, new PrintDispatchResult(firstJob, "HISTORY-PRINTER",
                index == 22 ? PrintDispatchOutcome.Unknown : PrintDispatchOutcome.Failed,
                true, index != 22, null, "FixtureOnly"), clock.Advance());
        }
        await Sql("""
            UPDATE master_data_sellable_items SET display_name='CURRENT-MENU-NAME',resolved_price='999' WHERE branch_id=$branch;
            UPDATE master_data_tables SET display_name='CURRENT-TABLE-NAME' WHERE branch_id=$branch;
            """);

        async Task Capture(string suffix, string table, IReadOnlyList<SubmitOrderLine> lines, bool cancel)
        {
            var session = "HISTORY-SESSION-" + suffix;
            Accepted(await commands.OpenTableAsync(new(Metadata("HISTORY-OPEN-" + suffix, session), TableId.From(table))));
            Accepted(await commands.SubmitOrderAsync(new(Metadata("HISTORY-ORDER-" + suffix, session), "HISTORY-BATCH-" + suffix, lines)));
            if (cancel)
            {
                var line = $"{branch}:HISTORY-ORDER-ONE:line:1";
                var reserved = await store.ExecuteAtomicallyAsync((transaction, token) => transaction.TryReserveCancellationAsync(branch,
                    session, "HISTORY-CANCEL-ONE", new Dictionary<string, int> { [line] = 2 }, token, "Original cancellation reason"), default);
                if (!reserved) { throw new InvalidOperationException("History fixture cancellation was not reserved."); }
                if (await store.QueueCancellationInspectionAsync(branch, "HISTORY-CANCEL-ONE", clock.Advance(), default) is null)
                { throw new InvalidOperationException("History fixture inspection was not queued."); }
                CancellationProductionCheck[] checkedLines = [new(line, 2, 0, 0, 0)];
                if (!await store.RecordCancellationProductionAsync(branch, "HISTORY-CANCEL-ONE", employee, clock.Advance(), checkedLines, default) ||
                    await store.ConfirmCancellationAsync(branch, "HISTORY-CANCEL-ONE", employee, clock.Advance(), checkedLines, new HashSet<string> { "ITEM-1" }, default) is null)
                { throw new InvalidOperationException("History fixture cancellation was not confirmed."); }
            }
            var current = await store.ExecuteAtomicallyAsync((transaction, token) => transaction.FindSessionAsync(branch, TableSessionId.From(session), token), default)
                ?? throw new InvalidOperationException("Missing history fixture session.");
            var payment = await commands.RequestPaymentAsync(new(Metadata("HISTORY-PAY-" + suffix, session), current.Version));
            Accepted(payment);
            var sale = await store.CompleteCashSaleAsync(branch, session, "HISTORY-CASH-" + suffix, employee, device,
                clock.Advance(), payment.TableVersion!.Value, 300, default)
                ?? throw new InvalidOperationException("History fixture cash capture failed.");
            if (sale.AppliedAmount != (cancel ? 100 : 200)) { throw new InvalidOperationException("Unexpected history fixture amount."); }
            await store.SimulateReceiptAsync(branch, sale.ReceiptNumber, !cancel, default);
        }
        async Task Sql(string sql)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, ForeignKeys = true }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand(); command.CommandText = sql;
            command.Parameters.AddWithValue("$branch", branch);
            command.Parameters.AddWithValue("$now", clock.Advance().ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }
    }
    private static void Accepted(BranchCommandReceipt receipt)
    {
        if (receipt.Outcome != BranchCommandOutcome.BranchAccepted)
        { throw new InvalidOperationException("History fixture command rejected: " + receipt.ErrorCode); }
    }
    private static DateOnly BranchDate(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
    private sealed class FixtureClock : IBranchClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset Advance() => UtcNow = UtcNow.AddMilliseconds(10);
    }
}
