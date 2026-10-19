using Microsoft.Data.Sqlite;
using Pos.Printing.Abstractions;

namespace Pos.Branch.Infrastructure.Sqlite.Tests;

public sealed class SqlitePrintDispatchStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Version_eight_database_adds_attempt_and_lease_schema()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeVersionEightAsync();

        await new SqliteTableSessionCommandStore(database.Path).InitializeAsync();

        Assert.Equal(11L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'print_attempts';
            """));
        Assert.Equal(3L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM pragma_table_info('print_jobs')
            WHERE name IN ('assigned_printer_id', 'lease_owner', 'lease_expires_at');
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'rendered_print_payloads';
            """));
    }

    [Fact]
    public async Task Only_one_worker_can_lease_a_printer_at_a_time()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync(jobCount: 2);
        var firstStore = new SqlitePrintDispatchStore(database.Path);
        var secondStore = new SqlitePrintDispatchStore(database.Path);

        var claims = await Task.WhenAll(
            firstStore.ClaimNextAsync(
                "PRINTER-01",
                "WORKER-01",
                Now,
                TimeSpan.FromMinutes(1)),
            secondStore.ClaimNextAsync(
                "PRINTER-01",
                "WORKER-02",
                Now,
                TimeSpan.FromMinutes(1)));

        Assert.Single(claims, claim => claim is not null);
        Assert.Single(claims, claim => claim is null);
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_jobs WHERE lease_owner IS NOT NULL;"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts;"));
    }

    [Fact]
    public async Task Active_lease_can_be_extended_atomically()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromSeconds(30)));

        var renewed = await store.RenewLeaseAsync(
            lease,
            Now.AddSeconds(20),
            TimeSpan.FromSeconds(30));

        Assert.Equal(Now.AddSeconds(50), renewed.LeaseExpiresAt);
        Assert.Equal(
            "2026-10-15T09:00:50.0000000+00:00",
            await database.ScalarAsync<string>(
                "SELECT lease_expires_at FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
    }

    [Fact]
    public async Task Stale_lease_token_cannot_replace_a_renewed_expiry()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromSeconds(30)));
        var renewed = await store.RenewLeaseAsync(
            lease,
            Now.AddSeconds(20),
            TimeSpan.FromSeconds(30));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RenewLeaseAsync(
                lease,
                Now.AddSeconds(25),
                TimeSpan.FromSeconds(60)));

        Assert.Contains("lease token", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            renewed.LeaseExpiresAt.ToUniversalTime().ToString("O"),
            await database.ScalarAsync<string>(
                "SELECT lease_expires_at FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
    }

    [Fact]
    public async Task Expired_or_released_lease_cannot_be_renewed()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromSeconds(30)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RenewLeaseAsync(
                lease,
                Now.AddSeconds(30),
                TimeSpan.FromSeconds(30)));

        var recovered = await store.RecoverExpiredLeasesAsync(Now.AddSeconds(30));
        var replacement = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-02",
            Now.AddSeconds(30),
            TimeSpan.FromSeconds(30)));
        await store.ReleaseBlockedAsync(replacement, "PaperOut");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RenewLeaseAsync(
                replacement,
                Now.AddSeconds(40),
                TimeSpan.FromSeconds(30)));
        Assert.Equal(1, recovered.ReleasedBeforeAttempt);
    }

    [Fact]
    public async Task Dispatching_lease_can_be_renewed_but_terminal_job_cannot()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromSeconds(30)));
        await store.StartAttemptAsync(
            lease,
            "ATTEMPT-01",
            new string('A', 64),
            Now);
        await store.MarkFirstByteSendingAsync(
            lease,
            "ATTEMPT-01",
            Now.AddSeconds(1));

        var renewed = await store.RenewLeaseAsync(
            lease,
            Now.AddSeconds(20),
            TimeSpan.FromSeconds(30));
        var result = new PrintDispatchResult(
            renewed.PrintJobId,
            renewed.PrinterId,
            PrintDispatchOutcome.PrintDispatched,
            true,
            false,
            null,
            null);
        await store.CompleteAttemptAsync(
            renewed,
            "ATTEMPT-01",
            result,
            Now.AddSeconds(21));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RenewLeaseAsync(
                renewed,
                Now.AddSeconds(25),
                TimeSpan.FromSeconds(60)));
        Assert.Equal("PrintDispatched", await database.ScalarAsync<string>(
            "SELECT status FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
    }

    [Fact]
    public async Task Renewal_rejects_nonpositive_or_nonextending_duration()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromSeconds(30)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.RenewLeaseAsync(lease, Now.AddSeconds(5), TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.RenewLeaseAsync(
                lease,
                Now.AddSeconds(5),
                TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task Blocked_preflight_releases_lease_without_creating_attempt()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromMinutes(1)));
        var lifecycle = new SqlitePrintDispatchLifecycle(
            store,
            lease,
            "ATTEMPT-01",
            new FixedTimeProvider(Now));
        var dispatcher = Dispatcher(database.Path, PrinterCondition.PaperOut, new FakeAdapter());

        var result = await dispatcher.DispatchAsync(Request(lease), lifecycle);
        await store.ReleaseBlockedAsync(lease, Assert.IsType<string>(result.BlockedReason));

        Assert.Equal(PrintDispatchOutcome.Blocked, result.Outcome);
        Assert.Equal("PaperOut", await database.ScalarAsync<string>(
            "SELECT blocked_reason FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts;"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_jobs WHERE lease_owner IS NOT NULL;"));
    }

    [Fact]
    public async Task First_byte_boundary_and_dispatch_result_are_persisted_atomically()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromMinutes(1)));
        var lifecycle = new SqlitePrintDispatchLifecycle(
            store,
            lease,
            "ATTEMPT-01",
            new FixedTimeProvider(Now.AddSeconds(1)));
        var dispatcher = Dispatcher(
            database.Path,
            PrinterCondition.Ready,
            new FakeAdapter { SignalFirstByte = true });

        var result = await dispatcher.DispatchAsync(Request(lease), lifecycle);
        await store.CompleteAttemptAsync(
            lease,
            lifecycle.PrintAttemptId,
            result,
            Now.AddSeconds(2));

        Assert.Equal(PrintDispatchOutcome.PrintDispatched, result.Outcome);
        Assert.Equal("PrintDispatched", await database.ScalarAsync<string>(
            "SELECT status FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
        Assert.Equal("PrintDispatched", await database.ScalarAsync<string>(
            "SELECT status FROM print_attempts WHERE print_attempt_id = 'ATTEMPT-01';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT attempt_count FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts WHERE first_byte_at IS NOT NULL;"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_jobs WHERE lease_owner IS NOT NULL;"));
    }

    [Fact]
    public async Task Failure_before_first_byte_is_retryable_only_after_backoff()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromMinutes(1)));
        var lifecycle = new SqlitePrintDispatchLifecycle(
            store,
            lease,
            "ATTEMPT-01",
            new FixedTimeProvider(Now));
        var dispatcher = Dispatcher(
            database.Path,
            PrinterCondition.Ready,
            new FakeAdapter { Failure = new IOException("connect failed") });

        var result = await dispatcher.DispatchAsync(Request(lease), lifecycle);
        await store.CompleteAttemptAsync(
            lease,
            lifecycle.PrintAttemptId,
            result,
            Now);

        var early = await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-02",
            Now.AddMilliseconds(999),
            TimeSpan.FromMinutes(1));
        var due = await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-02",
            Now.AddSeconds(1),
            TimeSpan.FromMinutes(1));

        Assert.Equal(PrintDispatchOutcome.Failed, result.Outcome);
        Assert.True(result.AutomaticRetryAllowed);
        Assert.Null(early);
        Assert.NotNull(due);
        Assert.Equal("PRINT-01", due.PrintJobId);
        Assert.Equal("Failed", await database.ScalarAsync<string>(
            "SELECT status FROM print_attempts WHERE print_attempt_id = 'ATTEMPT-01';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT automatic_retry_allowed FROM print_attempts WHERE print_attempt_id = 'ATTEMPT-01';"));
    }

    [Fact]
    public async Task Expired_connecting_attempt_becomes_failed_and_can_retry()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromSeconds(5)));
        await store.StartAttemptAsync(
            lease,
            "ATTEMPT-01",
            new string('A', 64),
            Now);

        var recovered = await store.RecoverExpiredLeasesAsync(Now.AddSeconds(5));
        var retry = await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-02",
            Now.AddSeconds(5),
            TimeSpan.FromMinutes(1));

        Assert.Equal(1, recovered.FailedBeforeFirstByte);
        Assert.Equal(0, recovered.MarkedUnknownAfterFirstByte);
        Assert.NotNull(retry);
        Assert.Equal("Failed", await database.ScalarAsync<string>(
            "SELECT status FROM print_attempts WHERE print_attempt_id = 'ATTEMPT-01';"));
    }

    [Fact]
    public async Task Expired_dispatching_attempt_becomes_unknown_without_retry()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromSeconds(5)));
        await store.StartAttemptAsync(
            lease,
            "ATTEMPT-01",
            new string('A', 64),
            Now);
        await store.MarkFirstByteSendingAsync(
            lease,
            "ATTEMPT-01",
            Now.AddSeconds(1));

        var recovered = await store.RecoverExpiredLeasesAsync(Now.AddSeconds(5));
        var retry = await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-02",
            Now.AddSeconds(5),
            TimeSpan.FromMinutes(1));

        Assert.Equal(1, recovered.MarkedUnknownAfterFirstByte);
        Assert.Null(retry);
        Assert.Equal("Unknown", await database.ScalarAsync<string>(
            "SELECT status FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
        Assert.Equal("Unknown", await database.ScalarAsync<string>(
            "SELECT status FROM print_attempts WHERE print_attempt_id = 'ATTEMPT-01';"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT automatic_retry_allowed FROM print_attempts WHERE print_attempt_id = 'ATTEMPT-01';"));
    }

    [Fact]
    public async Task Repeated_attempt_transitions_do_not_create_duplicates()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync();
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromMinutes(1)));
        var payloadHash = new string('A', 64);

        var firstNumber = await store.StartAttemptAsync(
            lease,
            "ATTEMPT-01",
            payloadHash,
            Now);
        var repeatedNumber = await store.StartAttemptAsync(
            lease,
            "ATTEMPT-01",
            payloadHash,
            Now);
        await store.MarkFirstByteSendingAsync(
            lease,
            "ATTEMPT-01",
            Now.AddSeconds(1));
        await store.MarkFirstByteSendingAsync(
            lease,
            "ATTEMPT-01",
            Now.AddSeconds(1));
        var result = new PrintDispatchResult(
            lease.PrintJobId,
            lease.PrinterId,
            PrintDispatchOutcome.PrintDispatched,
            true,
            false,
            null,
            null);
        await store.CompleteAttemptAsync(
            lease,
            "ATTEMPT-01",
            result,
            Now.AddSeconds(2));
        await store.CompleteAttemptAsync(
            lease,
            "ATTEMPT-01",
            result,
            Now.AddSeconds(2));

        Assert.Equal(1, firstNumber);
        Assert.Equal(firstNumber, repeatedNumber);
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_attempts;"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT attempt_count FROM print_jobs WHERE print_job_id = 'PRINT-01';"));
    }

    [Fact]
    public async Task Retry_limit_zero_leaves_failed_job_terminal()
    {
        await using var database = new DispatchDatabase();
        await database.InitializeAsync(maximumAutomaticRetriesBeforeSend: 0);
        var store = new SqlitePrintDispatchStore(database.Path);
        var lease = Assert.IsType<PrintJobLease>(await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-01",
            Now,
            TimeSpan.FromMinutes(1)));
        var lifecycle = new SqlitePrintDispatchLifecycle(
            store,
            lease,
            "ATTEMPT-01",
            new FixedTimeProvider(Now));
        var dispatcher = Dispatcher(
            database.Path,
            PrinterCondition.Ready,
            new FakeAdapter { Failure = new IOException("connect failed") });

        var result = await dispatcher.DispatchAsync(Request(lease), lifecycle);
        await store.CompleteAttemptAsync(
            lease,
            lifecycle.PrintAttemptId,
            result,
            Now);
        var retry = await store.ClaimNextAsync(
            "PRINTER-01",
            "WORKER-02",
            Now.AddDays(1),
            TimeSpan.FromMinutes(1));

        Assert.False(result.AutomaticRetryAllowed);
        Assert.Null(retry);
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM print_jobs WHERE next_retry_at IS NOT NULL;"));
    }

    private static SafePrintDispatcher Dispatcher(
        string databasePath,
        PrinterCondition condition,
        FakeAdapter adapter) =>
        new(
            new SqlitePrinterConfigurationProvider(databasePath),
            new StaticConditionProbe(condition),
            [adapter]);

    private static PrintDispatchRequest Request(PrintJobLease lease) =>
        new(
            lease.PrintJobId,
            lease.BranchId,
            lease.ProductionStationId,
            new RenderedPrintPayload(lease.SnapshotId, [0x1B, 0x40, 0x0A]));

    private sealed class StaticConditionProbe(PrinterCondition condition) : IPrinterConditionProbe
    {
        public Task<PrinterCondition> GetConditionAsync(
            PrinterProfile profile,
            CancellationToken cancellationToken) =>
            Task.FromResult(condition);
    }

    private sealed class FakeAdapter : IPrintTransportAdapter
    {
        public PrinterProtocol Protocol => PrinterProtocol.RawTcpEscPos;

        public bool SignalFirstByte { get; init; }

        public Exception? Failure { get; init; }

        public async Task SendAsync(
            PrinterProfile profile,
            RenderedPrintPayload payload,
            IPrintSendObserver observer,
            CancellationToken cancellationToken)
        {
            if (SignalFirstByte)
            {
                await observer.OnFirstByteSendingAsync(cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DispatchDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-print-dispatch-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync(
            int jobCount = 1,
            int maximumAutomaticRetriesBeforeSend = 3)
        {
            var store = new SqliteTableSessionCommandStore(Path);
            await store.InitializeAsync();
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
                    'printer-cashier.branch.test', 9100, 'RawTcpEscPos',
                    'epson-tm-t82x-m352a-v1', 'Epson', 'TM-T82X', 'M352A',
                    '13.07 ESC/POS', 80, 203, 576, 'BitmapRaster',
                    'UnicodeRaster', 'Unsupported', 0, '1D5601', NULL, NULL,
                    2000, 10000, {{maximumAutomaticRetriesBeforeSend}}, 1000, 1,
                    '2026-10-15T08:00:00.0000000+00:00');
                """);

            for (var index = 1; index <= jobCount; index++)
            {
                await ExecuteAsync($$"""
                    INSERT INTO bill_preview_snapshots (
                        snapshot_id, branch_id, request_id, table_session_id, table_id,
                        document_label, content_schema_version, content_json,
                        content_sha256, created_at)
                    VALUES (
                        'SNAPSHOT-{{index}}', 'BRANCH-01', 'REQUEST-{{index}}',
                        'SESSION-{{index}}', 'A{{index:00}}', 'Bill Preview', 1, '{}',
                        '{{new string('A', 64)}}',
                        '2026-10-15T08:0{{index}}:00.0000000+00:00');

                    INSERT INTO print_jobs (
                        print_job_id, branch_id, request_id, table_session_id,
                        snapshot_id, purpose, target_station, status,
                        attempt_count, created_at)
                    VALUES (
                        'PRINT-{{index:00}}', 'BRANCH-01', 'REQUEST-{{index}}',
                        'SESSION-{{index}}', 'SNAPSHOT-{{index}}', 'BillPreview',
                        'Cashier', 'PrintQueued', 0,
                        '2026-10-15T08:0{{index}}:00.0000000+00:00');
                    """);
            }
        }

        public Task InitializeVersionEightAsync() =>
            ExecuteAsync("""
                CREATE TABLE printer_profiles (
                    printer_id TEXT NOT NULL PRIMARY KEY);

                CREATE TABLE print_jobs (
                    print_job_id TEXT NOT NULL PRIMARY KEY,
                    status TEXT NOT NULL,
                    next_retry_at TEXT NULL,
                    created_at TEXT NOT NULL);

                PRAGMA user_version = 8;
                """);

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

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            await ValueTask.CompletedTask;
        }

        private async Task ExecuteAsync(string sql)
        {
            Directory.CreateDirectory(_directory);
            await using var connection = Connection(SqliteOpenMode.ReadWriteCreate);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
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
