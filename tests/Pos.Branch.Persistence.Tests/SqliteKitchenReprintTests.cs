using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteKitchenReprintTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "kitchen-copy-" + Guid.NewGuid().ToString("N") + ".db");
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static KitchenReprintCommand Command(string id = "copy") => new(id, "cash", "source", "line", "original-job",
        "device", "requester", "approver", "Requested copy\noriginal line", Now);
    private async Task Sql(string sql, params (string Name, object Value)[] values)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Foreign Keys=True"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) { command.Parameters.AddWithValue(name, value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Scalar(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
    private async Task<SqliteTableSessionCommandStore> Seed()
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            orderBatchId = "batch",
            tableSessionId = "session",
            tableId = "ORIGINAL",
            productionStationId = "station",
            submittedAt = Now,
            lines = new[] { new { orderLineId = "line", lineSequence = 3, displayName = "Original cancelled item", quantity = 2,
                fulfillmentType = "takeaway", comment = (string?)"original\ncomment", options = new[] { new { optionGroupId = (string?)null, displayName = "Original option", quantityPerItem = 2 } } },
                new { orderLineId = "other", lineSequence = 4, displayName = "Other line", quantity = 1, fulfillmentType = "dineIn", comment = (string?)null,
                    options = Array.Empty<object>().Select(_ => new { optionGroupId = (string?)null, displayName = "", quantityPerItem = 1 }).ToArray() } }
        });
        var sale = new CashSaleResult("session", "cash-employee", "cash-device", 3, "shift", "store", "2026-11-27", 100, 100, 0, "TEST-RECEIPT", "QueuedForSimulation");
        await Sql("""
            INSERT INTO table_sessions VALUES('branch','session','CURRENT','Completed',5);
            INSERT INTO cash_sales VALUES('branch','cash','session',$sale,'{}');
            INSERT INTO simulated_cash_receipt_jobs VALUES('branch','cash','TEST-RECEIPT',$time);
            INSERT INTO order_batches(branch_id,order_batch_id,request_id,table_session_id,device_id,employee_id,submitted_at)
                VALUES('branch','batch','submit','session','original-device','original-employee',$time);
            INSERT INTO production_order_snapshots VALUES('source','branch','submit','batch','session','ORIGINAL','station',1,$json,$hash,$time);
            INSERT INTO print_jobs(print_job_id,branch_id,request_id,table_session_id,snapshot_id,purpose,target_station,status,attempt_count,created_at)
                VALUES('original-job','branch','submit','session','source','KitchenOrder','station','Unknown',1,$time);
            """, ("$sale", JsonSerializer.Serialize(sale)), ("$time", Now.ToString("O")), ("$json", json),
            ("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))));
        return store;
    }
    private Task<string> Business() => Scalar("""
        SELECT (SELECT group_concat(result_json||bill_snapshot_json) FROM cash_sales)||':'||
            (SELECT group_concat(table_id||status||version) FROM table_sessions)||':'||
            (SELECT group_concat(content_json||content_sha256) FROM production_order_snapshots)||':'||
            (SELECT group_concat(status||attempt_count||snapshot_id) FROM print_jobs WHERE is_reprint=0)||':'||
            (SELECT count(*) FROM table_session_audit)||':'||(SELECT count(*) FROM cloud_sync_outbox)||':'||(SELECT count(*) FROM order_lines);
        """);

    [Fact]
    public async Task Queues_one_original_line_and_frozen_actor_audit_without_business_mutation()
    {
        var store = await Seed(); var before = await Business();
        var receipt = (await store.WriteKitchenReprintAsync("branch", Command()))!;
        Assert.Equal("line", receipt.OrderLineId); Assert.Equal("original-job", receipt.OriginalPrintJobId);
        Assert.Equal("station", receipt.ProductionStationId); Assert.Equal(before, await Business());
        using var copy = JsonDocument.Parse(await Scalar("SELECT content_json FROM kitchen_reprint_snapshots;"));
        var line = Assert.Single(copy.RootElement.GetProperty("lines").EnumerateArray());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32()); Assert.Equal(3, line.GetProperty("lineSequence").GetInt32());
        Assert.Equal("Original cancelled item", line.GetProperty("displayName").GetString());
        Assert.Equal("ORIGINAL", copy.RootElement.GetProperty("tableId").GetString());
        Assert.Equal("original\ncomment", line.GetProperty("comment").GetString());
        Assert.Equal("requester:approver:device:Queued", await Scalar("SELECT requester_employee_id||':'||approver_employee_id||':'||device_id||':'||outcome FROM kitchen_reprint_audit;"));
        var status = (await store.ReadKitchenReprintStatusAsync("branch", "requester", "device", "copy", Now))!;
        Assert.Equal("PrintQueued", status.Status); Assert.Equal(receipt, status.Receipt);
        Assert.Equal("ok", await Scalar("PRAGMA integrity_check;")); Assert.Equal("", await Scalar("PRAGMA foreign_key_check;"));
    }

    [Fact]
    public async Task Identical_retry_after_unknown_returns_same_job_and_changed_payload_conflicts()
    {
        var store = await Seed(); var command = Command(); var first = await store.WriteKitchenReprintAsync("branch", command);
        await Sql("UPDATE print_jobs SET status='Unknown' WHERE is_reprint=1;");
        Assert.Equal(first, await store.WriteKitchenReprintAsync("branch", command with { QueuedAt = Now.AddHours(1) }));
        foreach (var changed in new[] { command with { Reason = "different" }, command with { ApproverEmployeeId = "other" },
                     command with { DeviceId = "other" }, command with { OrderLineId = "other" } })
        { await Assert.ThrowsAsync<KitchenReprintConflictException>(() => store.WriteKitchenReprintAsync("branch", changed)); }
        Assert.Equal("1:1:1", await Scalar("SELECT (SELECT count(*) FROM kitchen_reprint_requests)||':'||(SELECT count(*) FROM kitchen_reprint_audit)||':'||(SELECT count(*) FROM print_jobs WHERE is_reprint=1);"));
        Assert.Equal("Unknown", (await store.ReadKitchenReprintStatusAsync("branch", "requester", "device", "copy", Now))!.Status);
    }

    [Fact]
    public async Task Concurrent_identical_commands_commit_once()
    {
        var store = await Seed(); var commands = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => store.WriteKitchenReprintAsync("branch", Command())));
        Assert.All(commands, receipt => Assert.Equal(commands[0], receipt)); Assert.Equal("1", await Scalar("SELECT count(*) FROM kitchen_reprint_audit;"));
    }

    [Fact]
    public async Task Missing_or_foreign_source_and_status_are_scoped_without_writes()
    {
        var store = await Seed(); var before = await Business();
        Assert.Null(await store.WriteKitchenReprintAsync("other-branch", Command()));
        Assert.Null(await store.WriteKitchenReprintAsync("branch", Command() with { CashRequestId = "missing" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.WriteKitchenReprintAsync("branch", Command() with { SnapshotId = "missing" }));
        await Assert.ThrowsAsync<KitchenReprintLineNotFoundException>(() => store.WriteKitchenReprintAsync("branch", Command() with { OrderLineId = "missing" }));
        await Assert.ThrowsAsync<CashHistoryPrintJobNotFoundException>(() => store.WriteKitchenReprintAsync("branch", Command() with { OriginalPrintJobId = "missing" }));
        Assert.Equal(before, await Business()); Assert.Equal("0", await Scalar("SELECT count(*) FROM kitchen_reprint_audit;"));
        await store.WriteKitchenReprintAsync("branch", Command());
        Assert.Null(await store.ReadKitchenReprintStatusAsync("other", "requester", "device", "copy", Now));
        Assert.Null(await store.ReadKitchenReprintStatusAsync("branch", "other", "device", "copy", Now));
        Assert.Null(await store.ReadKitchenReprintStatusAsync("branch", "requester", "other", "copy", Now));
    }

    [Fact]
    public async Task Corrupt_hash_and_transaction_failure_do_not_leave_partial_copy()
    {
        var store = await Seed();
        await Sql("CREATE TRIGGER fixture_audit_failure BEFORE INSERT ON kitchen_reprint_audit BEGIN SELECT RAISE(ABORT,'fixture rollback'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => store.WriteKitchenReprintAsync("branch", Command()));
        Assert.Equal("0:0:0", await Scalar("SELECT (SELECT count(*) FROM kitchen_reprint_snapshots)||':'||(SELECT count(*) FROM kitchen_reprint_requests)||':'||(SELECT count(*) FROM print_jobs WHERE is_reprint=1);"));
        await Sql("DROP TRIGGER fixture_audit_failure; UPDATE production_order_snapshots SET content_sha256=printf('%064d',0);");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WriteKitchenReprintAsync("branch", Command()));
        Assert.Equal("0", await Scalar("SELECT count(*) FROM kitchen_reprint_snapshots;"));
    }

    [Theory]
    [InlineData("kitchen_reprint_snapshots")]
    [InlineData("kitchen_reprint_requests")]
    [InlineData("kitchen_reprint_audit")]
    public async Task Captured_reprint_records_cannot_be_modified_or_deleted(string table)
    {
        var store = await Seed(); await store.WriteKitchenReprintAsync("branch", Command());
        await Assert.ThrowsAsync<SqliteException>(() => Sql($"UPDATE {table} SET request_id='different';"));
        await Assert.ThrowsAsync<SqliteException>(() => Sql($"DELETE FROM {table};"));
    }

    [Fact]
    public async Task Real_worker_renders_frozen_copy_and_never_retries_uncertain_send()
    {
        var store = await Seed(); var before = await Business();
        var receipt = (await store.WriteKitchenReprintAsync("branch", Command()))!;
        await Sql("""
            INSERT INTO printer_profiles(printer_id,branch_id,display_name,production_station_id,host,port,protocol,
                compatibility_profile_id,manufacturer,model,paper_width_millimeters,dpi,printable_width_dots,
                render_mode,thai_encoding_mode,status_mode,supports_cash_drawer,cutter_command_hex,
                connect_timeout_ms,send_timeout_ms,maximum_automatic_retries_before_send,automatic_retry_backoff_ms,is_active,updated_at)
            VALUES('printer','branch','Kitchen','station','printer.invalid',9100,'RawTcpEscPos',
                'epson-tm-t82x-m352a-v1','Epson','TM-T82X',80,203,576,'BitmapRaster','UnicodeRaster','Unsupported',0,
                '1D5601',2000,10000,3,1000,1,$time);
            """, ("$time", Now.ToString("O")));
        var configuration = new SqlitePrinterConfigurationProvider(_path);
        var raster = new CopyRasterizer(); var adapter = new UncertainAdapter();
        var worker = new PersistedPrintSpoolWorker(new(_path), new(_path), configuration,
            new EscPosRasterPayloadRenderer(raster), new SafePrintDispatcher(configuration, new ReadyProbe(), [adapter]),
            new FixedTime(), () => "copy-attempt");
        var result = await worker.ProcessNextAsync("printer", "worker", TimeSpan.FromMinutes(1));
        Assert.Equal(PrintSpoolCycleOutcome.Unknown, result.Outcome);
        Assert.Equal(receipt.ReprintPrintJobId, result.PrintJobId);
        Assert.Contains(raster.Lines, line => line.Text == "สำเนา / พิมพ์ซ้ำ");
        Assert.Contains(raster.Lines, line => line.Text == "โต๊ะ ORIGINAL");
        Assert.Contains(raster.Lines, line => line.Text == "2 x Original cancelled item");
        Assert.DoesNotContain(raster.Lines, line => line.Text.Contains("Other line", StringComparison.Ordinal));
        Assert.Equal("1:1", await Scalar("SELECT (SELECT count(*) FROM rendered_print_payloads)||':'||(SELECT count(*) FROM print_attempts);"));
        Assert.Equal("Unknown", (await store.ReadKitchenReprintStatusAsync("branch", "requester", "device", "copy", Now))!.Status);
        Assert.Equal(receipt, await store.WriteKitchenReprintAsync("branch", Command()));
        Assert.Equal(PrintSpoolCycleOutcome.NoWork, (await worker.ProcessNextAsync("printer", "worker", TimeSpan.FromMinutes(1))).Outcome);
        Assert.Equal(1, adapter.Sends); Assert.Equal(before, await Business());
    }

    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class ReadyProbe : IPrinterConditionProbe
    {
        public Task<PrinterCondition> GetConditionAsync(PrinterProfile profile, CancellationToken token) => Task.FromResult(PrinterCondition.Ready);
    }
    private sealed class CopyRasterizer : IEscPosDocumentRasterizer
    {
        public IReadOnlyList<EscPosTextLine> Lines { get; private set; } = [];
        public MonochromeRaster Rasterize(IReadOnlyList<EscPosTextLine> lines, int widthDots, CancellationToken token)
        { Lines = lines.ToArray(); return new(widthDots, 2, new byte[((widthDots + 7) / 8) * 2]); }
    }
    private sealed class UncertainAdapter : IPrintTransportAdapter
    {
        public PrinterProtocol Protocol => PrinterProtocol.RawTcpEscPos;
        public int Sends { get; private set; }
        public async Task SendAsync(PrinterProfile profile, RenderedPrintPayload payload, IPrintSendObserver observer, CancellationToken token)
        {
            Sends++; Assert.Equal("station", profile.ProductionStationId);
            await observer.OnFirstByteSendingAsync(token); throw new IOException("Simulated uncertainty after sending starts.");
        }
    }

    [Theory]
    [InlineData("receipt-target")]
    [InlineData("command-actor")]
    [InlineData("receipt-blob")]
    public async Task Corrupt_ledger_fails_closed_for_replay_and_status(string fault)
    {
        var store = await Seed(); await store.WriteKitchenReprintAsync("branch", Command());
        await Sql("DROP TRIGGER kitchen_reprint_requests_no_update;");
        await Sql(fault switch
        {
            "receipt-target" => "UPDATE kitchen_reprint_requests SET receipt_json=json_set(receipt_json,'$.CashRequestId','other');",
            "command-actor" => "UPDATE kitchen_reprint_requests SET command_json=json_set(command_json,'$.ApproverEmployeeId','other');",
            _ => "UPDATE kitchen_reprint_requests SET receipt_json=CAST(receipt_json AS BLOB);"
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadKitchenReprintStatusAsync("branch", "requester", "device", "copy", Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WriteKitchenReprintAsync("branch", Command()));
        Assert.Equal("1", await Scalar("SELECT count(*) FROM kitchen_reprint_audit;"));
    }

    [Fact]
    public async Task Version35_upgrade_preserves_originals_and_installs_reprint_guards()
    {
        var store = await Seed(); var before = await Business();
        await Sql("""
            DROP TRIGGER print_jobs_kitchen_reprint_insert_guard; DROP TRIGGER print_jobs_kitchen_reprint_update_guard;
            DROP TABLE kitchen_reprint_audit; DROP TABLE kitchen_reprint_requests; DROP TABLE kitchen_reprint_snapshots;
            PRAGMA user_version=35;
            """);
        await store.InitializeAsync(); Assert.Equal("36", await Scalar("PRAGMA user_version;")); Assert.Equal(before, await Business());
        var receipt = (await store.WriteKitchenReprintAsync("branch", Command()))!;
        await Assert.ThrowsAsync<SqliteException>(() => Sql($"UPDATE print_jobs SET is_reprint=0 WHERE print_job_id='{receipt.ReprintPrintJobId}';"));
        await Assert.ThrowsAsync<SqliteException>(() => Sql($"UPDATE print_jobs SET original_print_job_id=NULL WHERE print_job_id='{receipt.ReprintPrintJobId}';"));
        await Assert.ThrowsAsync<SqliteException>(() => Sql("UPDATE print_jobs SET is_reprint=1 WHERE print_job_id='original-job';"));
        Assert.Equal("ok", await Scalar("PRAGMA integrity_check;")); Assert.Equal("", await Scalar("PRAGMA foreign_key_check;"));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); } }
}
