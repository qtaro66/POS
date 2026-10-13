using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Application.TableSessions;
using Pos.Application.Time;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite.Tests;

public sealed class SqliteTableSessionCommandStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initialize_creates_durable_schema_with_expected_pragmas()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();

        Assert.Equal(2L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal("wal", await database.ScalarAsync<string>("PRAGMA journal_mode;"));
        Assert.Equal(2L, await database.ScalarAsync<long>("PRAGMA synchronous;"));
        Assert.Equal(store.DatabasePath, Path.GetFullPath(database.Path));
    }

    [Fact]
    public async Task Open_table_survives_restart_and_duplicate_returns_original_receipt()
    {
        await using var database = new TemporaryDatabase();
        var firstStore = await database.CreateStoreAsync();
        var firstService = Service(firstStore);
        var command = OpenCommand("REQ-1", "TS-1", "A04");

        var first = await firstService.OpenTableAsync(command);

        var restartedStore = await database.CreateStoreAsync();
        var duplicate = await Service(restartedStore).OpenTableAsync(command);

        Assert.Equal(first, duplicate);
        Assert.Equal(1L, await database.CountAsync("table_sessions"));
        Assert.Equal(1L, await database.CountAsync("command_receipts"));
        Assert.Equal(1L, await database.CountAsync("table_session_audit"));
        Assert.Equal(1L, await database.CountAsync("cloud_sync_outbox"));
    }

    [Fact]
    public async Task Request_payment_rehydrates_updates_and_replays_after_restart()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        await Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        var payment = PaymentCommand("REQ-2", "TS-1", expectedVersion: 1);

        var accepted = await Service(await database.CreateStoreAsync()).RequestPaymentAsync(payment);
        var replayed = await Service(await database.CreateStoreAsync()).RequestPaymentAsync(payment);

        Assert.Equal(accepted, replayed);
        Assert.Equal(TableSessionStatus.PaymentPending, accepted.SessionStatus);
        Assert.Equal(2, accepted.TableVersion);
        Assert.Equal("PaymentPending", await database.ScalarAsync<string>(
            "SELECT status FROM table_sessions WHERE table_session_id = 'TS-1';"));
        Assert.Equal(2L, await database.CountAsync("command_receipts"));
        Assert.Equal(2L, await database.CountAsync("table_session_audit"));
        Assert.Equal(2L, await database.CountAsync("cloud_sync_outbox"));
        Assert.Equal(1L, await database.CountAsync("bill_preview_snapshots"));
        Assert.Equal(1L, await database.CountAsync("print_jobs"));
        Assert.Equal("Pending", await database.ScalarAsync<string>(
            "SELECT processing_status FROM cloud_sync_outbox WHERE request_id = 'REQ-2';"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT retry_count FROM cloud_sync_outbox WHERE request_id = 'REQ-2';"));
        Assert.Equal("Cashier", await database.ScalarAsync<string>(
            "SELECT target_station FROM print_jobs WHERE request_id = 'REQ-2';"));
        Assert.Equal("PrintQueued", await database.ScalarAsync<string>(
            "SELECT status FROM print_jobs WHERE request_id = 'REQ-2';"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT attempt_count FROM print_jobs WHERE request_id = 'REQ-2';"));
        Assert.Equal(64L, await database.ScalarAsync<long>(
            "SELECT length(content_sha256) FROM bill_preview_snapshots WHERE request_id = 'REQ-2';"));
    }

    [Fact]
    public async Task Callback_failure_rolls_back_all_prior_writes()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        var receipt = Receipt("REQ-ROLLBACK");
        var session = TableSession.Open(
            TableSessionId.From("TS-1"),
            TableId.From("A04"),
            new TableSessionTransitionContext("EMPLOYEE-01", "DEVICE-01", Now));
        var transition = session.Transitions.Single();
        var previewContent = BillPreviewContent.FromJson(1, "{\"items\":[],\"netTotal\":0}");
        var snapshot = new BillPreviewSnapshot(
            "SNAPSHOT-ROLLBACK",
            "BRANCH-01",
            "REQ-ROLLBACK",
            session.Id,
            session.CurrentTableId,
            "ใบตรวจสอบรายการ / ยังไม่ใช่ใบเสร็จ",
            previewContent,
            Now);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ExecuteAtomicallyAsync<int>(async (transaction, token) =>
            {
                await transaction.InsertSessionAsync("BRANCH-01", session, token);
                await transaction.AddAuditAsync(
                    new TableSessionAuditRecord("AUDIT-ROLLBACK", "BRANCH-01", "REQ-ROLLBACK", transition),
                    token);
                await transaction.AddCloudOutboxAsync(
                    new CloudSyncOutboxRecord(
                        "OUTBOX-ROLLBACK",
                        "BRANCH-01",
                        "REQ-ROLLBACK",
                        session.Id,
                        transition.Action,
                        session.Version,
                        Now),
                    token);
                await transaction.AddBillPreviewSnapshotAsync(snapshot, token);
                await transaction.AddPrintJobAsync(
                    new PersistentPrintJob(
                        "PRINT-ROLLBACK",
                        "BRANCH-01",
                        "REQ-ROLLBACK",
                        session.Id,
                        snapshot.SnapshotId,
                        PrintJobPurpose.BillPreview,
                        PrintTargetStation.Cashier,
                        PersistentPrintJobStatus.PrintQueued,
                        0,
                        Now),
                    token);
                await transaction.AddReceiptAsync(receipt, token);
                throw new InvalidOperationException("Injected failure");
            }, CancellationToken.None));

        Assert.Equal("Injected failure", exception.Message);
        Assert.Equal(0L, await database.CountAsync("table_sessions"));
        Assert.Equal(0L, await database.CountAsync("command_receipts"));
        Assert.Equal(0L, await database.CountAsync("table_session_audit"));
        Assert.Equal(0L, await database.CountAsync("cloud_sync_outbox"));
        Assert.Equal(0L, await database.CountAsync("bill_preview_snapshots"));
        Assert.Equal(0L, await database.CountAsync("print_jobs"));
    }

    [Fact]
    public async Task Version_one_database_migrates_to_version_two_without_losing_sessions()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        await Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await database.ExecuteAsync("""
            DROP TABLE print_jobs;
            DROP TABLE bill_preview_snapshots;
            PRAGMA user_version = 1;
            """);

        var migratedStore = new SqliteTableSessionCommandStore(
            database.Path,
            new StaticBillPreviewContentSource());
        await migratedStore.InitializeAsync();

        Assert.Equal(2L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.CountAsync("table_sessions"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'bill_preview_snapshots';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'print_jobs';"));
    }

    [Fact]
    public async Task Content_capture_failure_leaves_session_ordering_and_creates_no_command_records()
    {
        await using var database = new TemporaryDatabase();
        var initialStore = await database.CreateStoreAsync();
        await Service(initialStore).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        var failingStore = await database.CreateStoreAsync(new ThrowingBillPreviewContentSource());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(failingStore).RequestPaymentAsync(PaymentCommand("REQ-2", "TS-1", 1)));

        Assert.Equal("Bill Preview source unavailable", exception.Message);
        Assert.Equal("Ordering", await database.ScalarAsync<string>(
            "SELECT status FROM table_sessions WHERE table_session_id = 'TS-1';"));
        Assert.Equal(1L, await database.CountAsync("command_receipts"));
        Assert.Equal(1L, await database.CountAsync("table_session_audit"));
        Assert.Equal(1L, await database.CountAsync("cloud_sync_outbox"));
        Assert.Equal(0L, await database.CountAsync("bill_preview_snapshots"));
        Assert.Equal(0L, await database.CountAsync("print_jobs"));
    }

    [Fact]
    public async Task Initialize_rejects_newer_schema_without_downgrading_version()
    {
        await using var database = new TemporaryDatabase();
        await database.ExecuteAsync("PRAGMA user_version = 3;");
        var store = new SqliteTableSessionCommandStore(database.Path, new StaticBillPreviewContentSource());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.InitializeAsync());

        Assert.Contains("schema version 3", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3L, await database.ScalarAsync<long>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task Two_store_instances_opening_same_table_receive_one_session()
    {
        await using var database = new TemporaryDatabase();
        var firstStore = await database.CreateStoreAsync();
        var secondStore = await database.CreateStoreAsync();

        var results = await Task.WhenAll(
            Service(firstStore).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04")),
            Service(secondStore).OpenTableAsync(OpenCommand("REQ-2", "TS-2", "A04")));

        Assert.Equal(results[0].TableSessionId, results[1].TableSessionId);
        Assert.Equal(1L, await database.CountAsync("table_sessions"));
        Assert.Equal(2L, await database.CountAsync("command_receipts"));
        Assert.Equal(1L, await database.CountAsync("table_session_audit"));
        Assert.Equal(1L, await database.CountAsync("cloud_sync_outbox"));
    }

    [Fact]
    public async Task Same_request_id_in_two_branches_has_independent_records()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();

        var first = await Service(store).OpenTableAsync(new OpenTableCommand(
            Metadata("REQ-1", "TS-1", "BRANCH-01"),
            TableId.From("A04")));
        var second = await Service(store).OpenTableAsync(new OpenTableCommand(
            Metadata("REQ-1", "TS-2", "BRANCH-02"),
            TableId.From("A04")));

        Assert.NotEqual(first.TableSessionId, second.TableSessionId);
        Assert.Equal(2L, await database.CountAsync("table_sessions"));
        Assert.Equal(2L, await database.CountAsync("command_receipts"));
        Assert.Equal(2L, await database.CountAsync("table_session_audit"));
        Assert.Equal(2L, await database.CountAsync("cloud_sync_outbox"));
    }

    [Fact]
    public async Task Persisted_open_cancellation_request_blocks_payment()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        await Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await database.ExecuteAsync("""
            INSERT INTO cancellation_requests (
                branch_id, cancellation_request_id, table_session_id, status)
            VALUES ('BRANCH-01', 'CANCEL-1', 'TS-1', 'ProductionStatusChecking');
            """);

        var result = await Service(await database.CreateStoreAsync())
            .RequestPaymentAsync(PaymentCommand("REQ-2", "TS-1", expectedVersion: 1));

        Assert.Equal(BranchCommandOutcome.Rejected, result.Outcome);
        Assert.Equal("OpenCancellationRequest", result.ErrorCode);
        Assert.Equal("Ordering", await database.ScalarAsync<string>(
            "SELECT status FROM table_sessions WHERE table_session_id = 'TS-1';"));
        Assert.Equal(1L, await database.CountAsync("table_session_audit"));
        Assert.Equal(1L, await database.CountAsync("cloud_sync_outbox"));
    }

    private static TableSessionCommandService Service(SqliteTableSessionCommandStore store) =>
        new(store, new FixedClock(Now));

    private static OpenTableCommand OpenCommand(string requestId, string sessionId, string tableId) =>
        new(Metadata(requestId, sessionId), TableId.From(tableId));

    private static RequestPaymentCommand PaymentCommand(
        string requestId,
        string sessionId,
        long expectedVersion) =>
        new(Metadata(requestId, sessionId), expectedVersion);

    private static BranchCommandMetadata Metadata(
        string requestId,
        string sessionId,
        string branchId = "BRANCH-01") =>
        new(
            requestId,
            branchId,
            "DEVICE-01",
            "EMPLOYEE-01",
            TableSessionId.From(sessionId),
            Now.AddMinutes(-1));

    private static BranchCommandReceipt Receipt(string requestId) =>
        new(
            requestId,
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            Now.AddMinutes(-1),
            BranchCommandOutcome.Rejected,
            TableSessionId.From("TS-1"),
            null,
            null,
            "Injected",
            Now);

    private sealed class FixedClock(DateTimeOffset utcNow) : IBranchClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-sqlite-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task<SqliteTableSessionCommandStore> CreateStoreAsync(
            ISqliteBillPreviewContentSource? contentSource = null)
        {
            var store = new SqliteTableSessionCommandStore(
                Path,
                contentSource ?? new StaticBillPreviewContentSource());
            await store.InitializeAsync();
            return store;
        }

        public Task<long> CountAsync(string tableName)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "table_sessions",
                "command_receipts",
                "table_session_audit",
                "cloud_sync_outbox",
                "bill_preview_snapshots",
                "print_jobs"
            };
            if (!allowed.Contains(tableName))
            {
                throw new ArgumentOutOfRangeException(nameof(tableName));
            }

            return ScalarAsync<long>($"SELECT COUNT(*) FROM {tableName};");
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
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

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private SqliteConnection Connection(SqliteOpenMode mode) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = mode,
                Pooling = false
            }.ToString());
    }

    private sealed class StaticBillPreviewContentSource : ISqliteBillPreviewContentSource
    {
        public Task<BillPreviewContent> CaptureAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            BillPreviewCaptureRequest request,
            CancellationToken cancellationToken)
        {
            Assert.Same(connection, transaction.Connection);
            return Task.FromResult(BillPreviewContent.FromJson(
                1,
                """
                {"items":[],"discounts":[],"charges":[],"vat":0,"roundingAdjustment":0,"netTotal":0}
                """));
        }
    }

    private sealed class ThrowingBillPreviewContentSource : ISqliteBillPreviewContentSource
    {
        public Task<BillPreviewContent> CaptureAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            BillPreviewCaptureRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Bill Preview source unavailable");
        }
    }
}
