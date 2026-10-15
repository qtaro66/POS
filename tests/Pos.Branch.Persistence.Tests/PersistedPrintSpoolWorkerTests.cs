using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Printing.Abstractions;

namespace Pos.Branch.Infrastructure.Sqlite.Tests;

public sealed class PersistedPrintSpoolWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_cycle_persists_payload_before_attempt_and_never_contacts_hardware()
    {
        await using var database = new SpoolDatabase();
        await database.InitializeAsync();
        var time = new MutableTimeProvider(Now);
        var renderer = new FakeRenderer([0x1B, 0x40, 0x0A]);
        var adapter = new FakeAdapter { SignalFirstByte = true };
        var worker = Worker(database.Path, time, renderer, adapter, PrinterCondition.Ready);

        var result = await worker.ProcessNextAsync(
            "PRINTER-01",
            "WORKER-01",
            TimeSpan.FromMinutes(1));

        Assert.Equal(PrintSpoolCycleOutcome.PrintDispatched, result.Outcome);
        Assert.Equal("PRINT-01", result.PrintJobId);
        Assert.Equal("ATTEMPT-01", result.PrintAttemptId);
        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(1, adapter.SendCount);
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM rendered_print_payloads;"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts;"));
        Assert.Equal("PrintDispatched", await database.ScalarAsync<string>(
            "SELECT status FROM print_jobs WHERE print_job_id = 'PRINT-01';"));

        var noWork = await worker.ProcessNextAsync(
            "PRINTER-01",
            "WORKER-01",
            TimeSpan.FromMinutes(1));
        Assert.Equal(PrintSpoolCycleOutcome.NoWork, noWork.Outcome);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task Blocked_preflight_reuses_same_spool_without_creating_attempt()
    {
        await using var database = new SpoolDatabase();
        await database.InitializeAsync();
        var time = new MutableTimeProvider(Now);
        var renderer = new FakeRenderer([0x1B, 0x40, 0x0A]);
        var adapter = new FakeAdapter { SignalFirstByte = true };
        var condition = new MutableConditionProbe(PrinterCondition.CoverOpen);
        var worker = Worker(database.Path, time, renderer, adapter, condition);

        var blocked = await worker.ProcessNextAsync(
            "PRINTER-01",
            "WORKER-01",
            TimeSpan.FromMinutes(1));

        Assert.Equal(PrintSpoolCycleOutcome.Blocked, blocked.Outcome);
        Assert.Equal("CoverOpen", blocked.Detail);
        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(0, adapter.SendCount);
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts;"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM rendered_print_payloads;"));

        condition.Condition = PrinterCondition.Ready;
        var dispatched = await worker.ProcessNextAsync(
            "PRINTER-01",
            "WORKER-02",
            TimeSpan.FromMinutes(1));

        Assert.Equal(PrintSpoolCycleOutcome.PrintDispatched, dispatched.Outcome);
        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(1, adapter.SendCount);
    }

    [Fact]
    public async Task Corrupt_source_snapshot_is_blocked_before_render_or_attempt()
    {
        await using var database = new SpoolDatabase();
        await database.InitializeAsync(contentHash: new string('A', 64));
        var renderer = new FakeRenderer([0x1B, 0x40, 0x0A]);
        var adapter = new FakeAdapter { SignalFirstByte = true };
        var worker = Worker(
            database.Path,
            new MutableTimeProvider(Now),
            renderer,
            adapter,
            PrinterCondition.Ready);

        var result = await worker.ProcessNextAsync(
            "PRINTER-01",
            "WORKER-01",
            TimeSpan.FromMinutes(1));

        Assert.Equal(PrintSpoolCycleOutcome.Blocked, result.Outcome);
        Assert.Equal("PayloadPreparationFailed:InvalidOperationException", result.Detail);
        Assert.Equal(0, renderer.CallCount);
        Assert.Equal(0, adapter.SendCount);
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM rendered_print_payloads;"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts;"));
    }

    [Fact]
    public async Task Retry_after_pre_send_failure_reuses_persisted_payload()
    {
        await using var database = new SpoolDatabase();
        await database.InitializeAsync();
        var time = new MutableTimeProvider(Now);
        var renderer = new FakeRenderer([0x1B, 0x40, 0x0A]);
        var adapter = new FakeAdapter
        {
            Failures = new Queue<Exception?>([new IOException("connect failed"), null]),
            SignalFirstByte = true
        };
        var worker = Worker(database.Path, time, renderer, adapter, PrinterCondition.Ready);

        var failed = await worker.ProcessNextAsync(
            "PRINTER-01",
            "WORKER-01",
            TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromSeconds(1));
        var retried = await worker.ProcessNextAsync(
            "PRINTER-01",
            "WORKER-02",
            TimeSpan.FromMinutes(1));

        Assert.Equal(PrintSpoolCycleOutcome.Failed, failed.Outcome);
        Assert.Equal(PrintSpoolCycleOutcome.PrintDispatched, retried.Outcome);
        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(2, adapter.SendCount);
        Assert.Equal(2L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts;"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM rendered_print_payloads;"));
    }

    [Fact]
    public async Task Persisted_payload_is_immutable_and_hash_verified_on_read()
    {
        await using var database = new SpoolDatabase();
        await database.InitializeAsync();
        var dispatch = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await dispatch.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromMinutes(1)));
        var store = new SqliteRenderedPrintPayloadStore(database.Path);
        var payload = new RenderedPrintPayload("SNAPSHOT-01", [0x1B, 0x40, 0x0A]);

        await store.SaveAsync(lease, payload, Now);
        var loaded = Assert.IsType<RenderedPrintPayload>(await store.FindAsync(lease, Now));

        Assert.Equal(payload.Sha256, loaded.Sha256);
        Assert.True(payload.Bytes.Span.SequenceEqual(loaded.Bytes.Span));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(
            lease,
            new RenderedPrintPayload("SNAPSHOT-01", [0x1B, 0x40, 0x0D]),
            Now));
        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            UPDATE rendered_print_payloads
            SET payload_bytes = X'00'
            WHERE print_job_id = 'PRINT-01';
            """));
    }

    private static PersistedPrintSpoolWorker Worker(
        string databasePath,
        MutableTimeProvider time,
        FakeRenderer renderer,
        FakeAdapter adapter,
        PrinterCondition condition) =>
        Worker(databasePath, time, renderer, adapter, new MutableConditionProbe(condition));

    private static PersistedPrintSpoolWorker Worker(
        string databasePath,
        MutableTimeProvider time,
        FakeRenderer renderer,
        FakeAdapter adapter,
        MutableConditionProbe condition)
    {
        var configuration = new SqlitePrinterConfigurationProvider(databasePath);
        return new PersistedPrintSpoolWorker(
            new SqlitePrintDispatchStore(databasePath),
            new SqliteRenderedPrintPayloadStore(databasePath),
            configuration,
            renderer,
            new SafePrintDispatcher(configuration, condition, [adapter]),
            time,
            () => $"ATTEMPT-{adapter.SendCount + 1:00}");
    }

    private sealed class FakeRenderer(byte[] bytes) : IPrintPayloadRenderer
    {
        public int CallCount { get; private set; }

        public Task<RenderedPrintPayload> RenderAsync(
            PrintRenderRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Assert.Equal(PrintDocumentKind.BillPreview, request.Document.Kind);
            Assert.Equal("SNAPSHOT-01", request.Document.DocumentId);
            Assert.Equal("PRINTER-01", request.Printer.PrinterId);
            return Task.FromResult(new RenderedPrintPayload(request.Document.DocumentId, bytes));
        }
    }

    private sealed class MutableConditionProbe(PrinterCondition condition) : IPrinterConditionProbe
    {
        public PrinterCondition Condition { get; set; } = condition;

        public Task<PrinterCondition> GetConditionAsync(
            PrinterProfile profile,
            CancellationToken cancellationToken) =>
            Task.FromResult(Condition);
    }

    private sealed class FakeAdapter : IPrintTransportAdapter
    {
        public PrinterProtocol Protocol => PrinterProtocol.RawTcpEscPos;

        public bool SignalFirstByte { get; init; }

        public Queue<Exception?> Failures { get; init; } = [];

        public int SendCount { get; private set; }

        public async Task SendAsync(
            PrinterProfile profile,
            RenderedPrintPayload payload,
            IPrintSendObserver observer,
            CancellationToken cancellationToken)
        {
            SendCount++;
            var failure = Failures.Count > 0 ? Failures.Dequeue() : null;
            if (failure is not null)
            {
                throw failure;
            }

            if (SignalFirstByte)
            {
                await observer.OnFirstByteSendingAsync(cancellationToken);
            }
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }

    private sealed class SpoolDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-print-spool-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync(string? contentHash = null)
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
            const string json = "{\"document\":\"bill-preview\"}";
            contentHash ??= Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            await ExecuteAsync($$"""
                INSERT INTO printer_profiles (
                    printer_id, branch_id, display_name, production_station_id,
                    host, port, protocol, compatibility_profile_id,
                    manufacturer, model, product_code, firmware_version,
                    paper_width_millimeters, dpi, printable_width_dots,
                    render_mode, thai_encoding_mode, status_mode, supports_cash_drawer,
                    cutter_command_hex, drawer_pulse_command_hex, status_command_profile,
                    connect_timeout_ms, send_timeout_ms,
                    maximum_automatic_retries_before_send, automatic_retry_backoff_ms,
                    is_active, updated_at)
                VALUES (
                    'PRINTER-01', 'BRANCH-01', 'แคชเชียร์', 'Cashier',
                    'printer.invalid', 9100, 'RawTcpEscPos',
                    'epson-tm-t82x-m352a-v1', 'Epson', 'TM-T82X', 'M352A',
                    '13.07 ESC/POS', 80, 203, 576, 'BitmapRaster',
                    'UnicodeRaster', 'Unsupported', 0, '1D5601', NULL, NULL,
                    2000, 10000, 3, 1000, 1,
                    '2026-10-15T10:00:00.0000000+00:00');

                INSERT INTO bill_preview_snapshots (
                    snapshot_id, branch_id, request_id, table_session_id, table_id,
                    document_label, content_schema_version, content_json,
                    content_sha256, created_at)
                VALUES (
                    'SNAPSHOT-01', 'BRANCH-01', 'REQUEST-01', 'SESSION-01', 'A04',
                    'Bill Preview', 1, '{{json}}', '{{contentHash}}',
                    '2026-10-15T10:00:00.0000000+00:00');

                INSERT INTO print_jobs (
                    print_job_id, branch_id, request_id, table_session_id,
                    snapshot_id, purpose, target_station, status,
                    attempt_count, created_at)
                VALUES (
                    'PRINT-01', 'BRANCH-01', 'REQUEST-01', 'SESSION-01',
                    'SNAPSHOT-01', 'BillPreview', 'Cashier', 'PrintQueued', 0,
                    '2026-10-15T10:00:00.0000000+00:00');
                """);
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(
                value!,
                typeof(T),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task ExecuteAsync(string sql)
        {
            Directory.CreateDirectory(_directory);
            await using var connection = Connection(SqliteOpenMode.ReadWriteCreate);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            await ValueTask.CompletedTask;
        }

        private SqliteConnection Connection(SqliteOpenMode mode) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = mode,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
    }
}
