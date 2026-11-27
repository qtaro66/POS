using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Printing.Abstractions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteCashHistoryPrintEvidenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cash-print-evidence-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
    private const string SnapshotId = "branch:submit:production:1";
    private const string JobId = "branch:submit:production-print:1";
    private static string Timestamp(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);
    private async Task Sql(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) { command.Parameters.AddWithValue(name, value); }
        await command.ExecuteNonQueryAsync();
    }
    private async Task<string> Text(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }
    private async Task<SqliteTableSessionCommandStore> Seed(bool job = true)
    {
        var store = new SqliteTableSessionCommandStore(_path); await store.InitializeAsync();
        var sale = new CashSaleResult("session", "cash-employee", "cash-device", 3, "shift", "store", "2026-11-27",
            100, 100, 0, "TEST-CASH", "QueuedForSimulation");
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            orderBatchId = "batch",
            tableSessionId = "session",
            tableId = "ORIGINAL",
            productionStationId = "kitchen",
            submittedAt = Now,
            lines = new[] { new { orderLineId = "line", lineSequence = 1, displayName = "Original item", quantity = 2,
                fulfillmentType = "dineIn", comment = (string?)null, options = Array.Empty<object>() } }
        });
        await Sql("""
            INSERT INTO table_sessions VALUES('branch','session','CURRENT','Completed',5);
            INSERT INTO cash_sales VALUES('branch','cash','session',$sale,'{}');
            INSERT INTO simulated_cash_receipt_jobs VALUES('branch','cash','TEST-CASH',$time);
            INSERT INTO order_batches(branch_id,order_batch_id,request_id,table_session_id,device_id,employee_id,submitted_at)
                VALUES('branch','batch','submit','session','submit-device','submit-employee',$time);
            INSERT INTO production_order_snapshots VALUES($snapshot,'branch','submit','batch','session','ORIGINAL','kitchen',1,$json,$hash,$time);
            INSERT INTO printer_profiles (
                printer_id,branch_id,display_name,production_station_id,host,port,protocol,compatibility_profile_id,
                manufacturer,model,product_code,firmware_version,paper_width_millimeters,dpi,printable_width_dots,
                render_mode,thai_encoding_mode,status_mode,supports_cash_drawer,cutter_command_hex,drawer_pulse_command_hex,
                status_command_profile,connect_timeout_ms,send_timeout_ms,maximum_automatic_retries_before_send,
                automatic_retry_backoff_ms,is_active,updated_at)
            VALUES('printer','branch','Original printer','kitchen','printer.invalid',9100,'RawTcpEscPos',
                'epson-tm-t82x-m352a-v1','Epson','TM-T82X','M352A','13.07 ESC/POS',80,203,576,
                'BitmapRaster','UnicodeRaster','Unsupported',0,'1D5601',NULL,NULL,2000,10000,3,1000,1,$time);
            """, ("$sale", JsonSerializer.Serialize(sale)), ("$time", Timestamp(Now)), ("$snapshot", SnapshotId),
            ("$json", json), ("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))));
        if (job) { await AddOriginalJob(); }
        return store;
    }
    private Task AddOriginalJob() => Sql("""
        INSERT INTO print_jobs(print_job_id,branch_id,request_id,table_session_id,snapshot_id,purpose,target_station,status,attempt_count,created_at)
        VALUES($job,'branch','submit','session',$snapshot,'KitchenOrder','kitchen','PrintQueued',0,$time);
        """, ("$job", JobId), ("$snapshot", SnapshotId), ("$time", Timestamp(Now)));
    private async Task Attempt(string id, int number, string status, bool retry = false)
    {
        var first = status is "Dispatching" or "PrintDispatched" ? Timestamp(Now.AddSeconds(1)) : null;
        var finish = status is "Failed" or "Unknown" or "PrintDispatched" ? Timestamp(Now.AddSeconds(2)) : null;
        await Sql("""
            INSERT INTO print_attempts(print_attempt_id,print_job_id,printer_id,attempt_number,worker_id,payload_sha256,
                status,automatic_retry_allowed,error_code,started_at,first_byte_at,finished_at)
            VALUES($id,$job,'printer',$number,'PRIVATE-WORKER',$hash,$status,$retry,'PRIVATE-ERROR',$time,$first,$finish);
            UPDATE print_jobs SET attempt_count=max(attempt_count,$number) WHERE print_job_id=$job;
            """, ("$id", id), ("$job", JobId), ("$number", number), ("$hash", new string('A', 64)),
            ("$status", status), ("$retry", retry ? 1 : 0), ("$time", Timestamp(Now)),
            ("$first", (object?)first ?? DBNull.Value), ("$finish", (object?)finish ?? DBNull.Value));
    }

    [Theory]
    [InlineData("PrintQueued")]
    [InlineData("Dispatching")]
    [InlineData("PrintDispatched")]
    [InlineData("Failed")]
    [InlineData("Unknown")]
    [InlineData("Completed")]
    public async Task Job_status_is_an_observation_of_original_submission_without_current_configuration_or_writes(string status)
    {
        var store = await Seed();
        await Sql("UPDATE print_jobs SET status=$status,last_error='PRIVATE-ERROR',blocked_reason='PRIVATE-BLOCK',lease_owner='PRIVATE-WORKER';", ("$status", status));
        await Sql("UPDATE printer_profiles SET display_name='PRIVATE-CURRENT',host='private.invalid',production_station_id='CURRENT'; UPDATE table_sessions SET table_id='MOVED';");
        var before = await File.ReadAllBytesAsync(_path);
        var page = (await store.ReadCashHistoryPrintEvidenceAsync("branch", "cash", SnapshotId, Now))!;
        var captured = Assert.IsType<CashHistoryPrintJob>(page.Job);
        Assert.Equal(status, captured.Status); Assert.Equal("kitchen", captured.ProductionStationId);
        Assert.Equal("submit", captured.RequestId); Assert.Equal(SnapshotId, captured.SnapshotId);
        Assert.False(captured.IsReprint); Assert.Null(captured.OriginalPrintJobId);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(page));
        Assert.DoesNotContain("printer.invalid", JsonSerializer.Serialize(page));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Unknown")]
    [InlineData("PrintDispatched")]
    public async Task Real_dispatch_store_transitions_remain_readable_without_replaying_or_sending(string terminal)
    {
        var store = await Seed(); var dispatch = new SqlitePrintDispatchStore(_path);
        var lease = (await dispatch.ClaimNextAsync("printer", "worker", Now, TimeSpan.FromSeconds(30)))!;
        Assert.Equal(1, await dispatch.StartAttemptAsync(lease, "attempt-real", new string('B', 64), Now));
        var connecting = (await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, new()))!;
        Assert.Equal("Connecting", Assert.Single(connecting.Items).Status);
        if (terminal != "Failed")
        {
            await dispatch.MarkFirstByteSendingAsync(lease, "attempt-real", Now.AddSeconds(1));
            var sending = (await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now.AddSeconds(1), new()))!;
            Assert.Equal("Dispatching", Assert.Single(sending.Items).Status);
            Assert.NotNull(sending.Items[0].FirstByteAt);
        }
        await dispatch.CompleteAttemptAsync(lease, "attempt-real", new PrintDispatchResult(JobId, "printer",
            Enum.Parse<PrintDispatchOutcome>(terminal), true, terminal == "Failed", null, "PRIVATE-ERROR"), Now.AddSeconds(2));
        var before = await File.ReadAllBytesAsync(_path);
        var captured = (await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now.AddSeconds(2), new()))!;
        var attempt = Assert.Single(captured.Items);
        Assert.Equal(terminal, attempt.Status); Assert.Equal(terminal, captured.Job.Status);
        Assert.Equal(terminal == "Failed", attempt.AutomaticRetryAllowed);
        Assert.NotNull(attempt.FinishedAt);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(captured));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Fact]
    public async Task Missing_evidence_is_distinct_from_missing_cash_snapshot_or_job_and_all_scopes_are_fixed()
    {
        var store = await Seed(job: false);
        Assert.Null((await store.ReadCashHistoryPrintEvidenceAsync("branch", "cash", SnapshotId, Now))!.Job);
        Assert.Null(await store.ReadCashHistoryPrintEvidenceAsync("other", "cash", SnapshotId, Now));
        Assert.Null(await store.ReadCashHistoryPrintAttemptsAsync("branch", "missing", SnapshotId, JobId, Now, new()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ReadCashHistoryPrintEvidenceAsync("branch", "cash", "missing", Now));
        await Assert.ThrowsAsync<CashHistoryPrintJobNotFoundException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, new()));
        await AddOriginalJob();
        Assert.Empty((await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, new()))!.Items);
        var foreign = new CashSaleResult("foreign-session", "employee", "device", 3, "shift", "store", "2026-11-27",
            100, 100, 0, "TEST-FOREIGN", "QueuedForSimulation");
        await Sql("""
            INSERT INTO table_sessions VALUES('branch','foreign-session','FOREIGN','Completed',5);
            INSERT INTO cash_sales VALUES('branch','foreign-cash','foreign-session',$sale,'{}');
            INSERT INTO simulated_cash_receipt_jobs VALUES('branch','foreign-cash','TEST-FOREIGN',$time);
            """, ("$sale", JsonSerializer.Serialize(foreign)), ("$time", Timestamp(Now)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ReadCashHistoryPrintEvidenceAsync("branch", "foreign-cash", SnapshotId, Now));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "foreign-cash", SnapshotId, JobId, Now, new()));
    }

    [Fact]
    public async Task Attempt_continuation_freezes_membership_but_preserves_current_observations_and_binds_subject()
    {
        var store = await Seed(); await Attempt("first", 1, "Failed", true); await Attempt("second", 2, "Connecting");
        var query = new CashHistoryPrintQuery(Limit: 1);
        var first = (await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query))!;
        Assert.Equal("second", Assert.Single(first.Items).PrintAttemptId); Assert.NotNull(first.NextCursor);
        await Sql("UPDATE print_attempts SET status='Failed',finished_at=$time,automatic_retry_allowed=1 WHERE print_attempt_id='second';", ("$time", Timestamp(Now)));
        var updated = (await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query))!;
        Assert.Equal("Failed", Assert.Single(updated.Items).Status);
        await Attempt("later", 3, "Connecting");
        var second = (await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query with { Cursor = first.NextCursor }))!;
        Assert.Equal("first", Assert.Single(second.Items).PrintAttemptId); Assert.Null(second.NextCursor);
        Assert.Equal(3, second.Job.AttemptCount);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query with { Limit = 2, Cursor = first.NextCursor }));
        var normalized = first.NextCursor!.Replace('-', '+').Replace('_', '/');
        var cursor = JsonNode.Parse(Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')))!;
        cursor["ScopeHash"] = new string('0', 64);
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query with { Cursor = forged }));
        await Sql("VACUUM;");
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query with { Cursor = first.NextCursor }));
        var refreshed = (await store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query))!;
        Assert.NotNull(refreshed.NextCursor);
        await Sql("CREATE TABLE unrelated_schema_cookie_change(value TEXT);");
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, query with { Cursor = refreshed.NextCursor }));
    }

    [Fact]
    public async Task Long_unicode_captured_scope_at_identifier_bounds_has_a_bounded_round_trippable_cursor()
    {
        var store = await Seed(job: false);
        var branch = new string('ก', 256); var request = new string('ข', 256); var session = new string('ค', 256);
        var snapshot = new string('ง', 1024); var job = new string('จ', 1024);
        var sale = new CashSaleResult(session, "employee", "device", 3, "shift", "store", "2026-11-27", 100, 100, 0, "TEST-LONG", "QueuedForSimulation");
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            orderBatchId = "batch-long",
            tableSessionId = session,
            tableId = "LONG-TABLE",
            productionStationId = "kitchen",
            submittedAt = Now,
            lines = new[] { new { orderLineId = "line-long", lineSequence = 1, displayName = "Original", quantity = 1,
                fulfillmentType = "dineIn", comment = (string?)null, options = Array.Empty<object>() } }
        });
        await Sql("""
            INSERT INTO table_sessions VALUES($branch,$session,'LONG-TABLE','Completed',5);
            INSERT INTO cash_sales VALUES($branch,$request,$session,$sale,'{}');
            INSERT INTO simulated_cash_receipt_jobs VALUES($branch,$request,'TEST-LONG',$time);
            INSERT INTO order_batches(branch_id,order_batch_id,request_id,table_session_id,device_id,employee_id,submitted_at)
                VALUES($branch,'batch-long','submit-long',$session,'device','employee',$time);
            INSERT INTO production_order_snapshots VALUES($snapshot,$branch,'submit-long','batch-long',$session,'LONG-TABLE','kitchen',1,$json,$hash,$time);
            INSERT INTO printer_profiles (
                printer_id,branch_id,display_name,production_station_id,host,port,protocol,compatibility_profile_id,
                manufacturer,model,product_code,firmware_version,paper_width_millimeters,dpi,printable_width_dots,
                render_mode,thai_encoding_mode,status_mode,supports_cash_drawer,cutter_command_hex,drawer_pulse_command_hex,
                status_command_profile,connect_timeout_ms,send_timeout_ms,maximum_automatic_retries_before_send,
                automatic_retry_backoff_ms,is_active,updated_at)
            SELECT 'printer-long',$branch,display_name,production_station_id,host,port,protocol,compatibility_profile_id,
                manufacturer,model,product_code,firmware_version,paper_width_millimeters,dpi,printable_width_dots,
                render_mode,thai_encoding_mode,status_mode,supports_cash_drawer,cutter_command_hex,drawer_pulse_command_hex,
                status_command_profile,connect_timeout_ms,send_timeout_ms,maximum_automatic_retries_before_send,
                automatic_retry_backoff_ms,is_active,updated_at FROM printer_profiles WHERE printer_id='printer';
            INSERT INTO print_jobs(print_job_id,branch_id,request_id,table_session_id,snapshot_id,purpose,target_station,status,attempt_count,created_at)
                VALUES($job,$branch,'submit-long',$session,$snapshot,'KitchenOrder','kitchen','Failed',2,$time);
            INSERT INTO print_attempts(print_attempt_id,print_job_id,printer_id,attempt_number,worker_id,payload_sha256,
                status,automatic_retry_allowed,started_at,finished_at)
                VALUES('long-first',$job,'printer-long',1,'worker',$hash,'Failed',1,$time,$time),
                      ('long-second',$job,'printer-long',2,'worker',$hash,'Failed',1,$time,$time);
            """, ("$branch", branch), ("$request", request), ("$session", session), ("$snapshot", snapshot), ("$job", job),
            ("$sale", JsonSerializer.Serialize(sale)), ("$json", json), ("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))),
            ("$time", Timestamp(Now)));
        var first = (await store.ReadCashHistoryPrintAttemptsAsync(branch, request, snapshot, job, Now, new(Limit: 1)))!;
        Assert.Equal("long-second", Assert.Single(first.Items).PrintAttemptId);
        Assert.NotNull(first.NextCursor); Assert.InRange(first.NextCursor.Length, 1, 4096);
        var second = (await store.ReadCashHistoryPrintAttemptsAsync(branch, request, snapshot, job, Now, new(Limit: 1, Cursor: first.NextCursor)))!;
        Assert.Equal("long-first", Assert.Single(second.Items).PrintAttemptId); Assert.Null(second.NextCursor);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(51, null)]
    [InlineData(1, "not a cursor")]
    [InlineData(1, "")]
    public async Task Invalid_attempt_queries_do_not_fall_back_to_broad_reads(int limit, string? cursor)
    {
        var store = await Seed(); var before = await File.ReadAllBytesAsync(_path);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now,
            new CashHistoryPrintQuery(limit, cursor)));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("request")]
    [InlineData("timestamp")]
    [InlineData("snapshot-hash")]
    [InlineData("reprint-parent")]
    public async Task Invalid_selected_job_or_snapshot_is_unavailable_without_repair(string fault)
    {
        var store = await Seed();
        switch (fault)
        {
            case "target":
                var payload = JsonNode.Parse(await Text("SELECT content_json FROM production_order_snapshots;"))!;
                payload["productionStationId"] = "WRONG"; var changed = payload.ToJsonString();
                await Sql("UPDATE production_order_snapshots SET production_station_id='WRONG',content_json=$json,content_sha256=$hash;",
                    ("$json", changed), ("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(changed))))); break;
            case "request": await Sql("UPDATE production_order_snapshots SET request_id='WRONG';"); break;
            case "timestamp": await Sql("UPDATE print_jobs SET created_at='2026-11-27';"); break;
            case "snapshot-hash": await Sql("UPDATE production_order_snapshots SET content_sha256=$hash;", ("$hash", new string('0', 64))); break;
            case "reprint-parent": await Sql("UPDATE print_jobs SET is_reprint=1,original_print_job_id=print_job_id;"); break;
        }
        var before = await File.ReadAllBytesAsync(_path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryPrintEvidenceAsync("branch", "cash", SnapshotId, Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, new()));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    [Theory]
    [InlineData("unknown-retry")]
    [InlineData("failed-first-byte")]
    [InlineData("timestamp")]
    [InlineData("count")]
    [InlineData("identifier")]
    public async Task Invalid_attempts_are_unavailable_without_suggesting_unsafe_retry(string fault)
    {
        var store = await Seed(); await Attempt("attempt", 1, "Connecting");
        switch (fault)
        {
            case "unknown-retry": await Sql("UPDATE print_attempts SET status='Unknown',finished_at=$time,automatic_retry_allowed=1;", ("$time", Timestamp(Now))); break;
            case "failed-first-byte": await Sql("UPDATE print_attempts SET status='Failed',finished_at=$time,first_byte_at=$time;", ("$time", Timestamp(Now))); break;
            case "timestamp": await Sql("UPDATE print_attempts SET started_at='2026-11-27';"); break;
            case "count": await Sql("UPDATE print_jobs SET attempt_count=0;"); break;
            case "identifier": await Sql("UPDATE print_attempts SET print_attempt_id='bad' || char(133);"); break;
        }
        var before = await File.ReadAllBytesAsync(_path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadCashHistoryPrintAttemptsAsync("branch", "cash", SnapshotId, JobId, Now, new()));
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) { File.Delete(_path + suffix); }
    }
}
