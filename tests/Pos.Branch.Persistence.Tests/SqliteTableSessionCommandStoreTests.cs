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

        Assert.Equal(5L, await database.ScalarAsync<long>("PRAGMA user_version;"));
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
        Assert.Equal(1L, await database.CountAsync("table_session_billing_policy"));
        Assert.Equal("MASTER-1", await database.ScalarAsync<string>(
            "SELECT master_data_version FROM table_session_billing_policy WHERE table_session_id = 'TS-1';"));
    }

    [Fact]
    public async Task Open_table_without_active_master_data_is_idempotently_rejected()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync(seedActiveMasterData: false);
        var command = OpenCommand("REQ-1", "TS-1", "A04");

        var first = await Service(store).OpenTableAsync(command);
        var duplicate = await Service(store).OpenTableAsync(command);

        Assert.Equal(BranchCommandOutcome.Rejected, first.Outcome);
        Assert.Equal("ActiveMasterDataUnavailable", first.ErrorCode);
        Assert.Equal(first, duplicate);
        Assert.Equal(0L, await database.CountAsync("table_sessions"));
        Assert.Equal(0L, await database.CountAsync("table_session_billing_policy"));
        Assert.Equal(1L, await database.CountAsync("command_receipts"));
        Assert.Equal(0L, await database.CountAsync("table_session_audit"));
        Assert.Equal(0L, await database.CountAsync("cloud_sync_outbox"));
    }

    [Fact]
    public async Task Existing_session_keeps_original_version_and_new_session_uses_new_active_package()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        await Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await database.ActivateMasterDataAsync("BRANCH-01", "MASTER-2", "CALC-2", 8m, 5m);

        var existing = await Service(store).OpenTableAsync(OpenCommand("REQ-2", "TS-2", "A04"));
        var newlyOpened = await Service(store).OpenTableAsync(OpenCommand("REQ-3", "TS-3", "A05"));
        await database.ActivateMasterDataAsync("BRANCH-01", "MASTER-3", "CALC-3", 9m, 6m);

        Assert.Equal("TS-1", existing.TableSessionId.Value);
        Assert.Equal("TS-3", newlyOpened.TableSessionId.Value);
        Assert.Equal("MASTER-1", await database.ScalarAsync<string>(
            "SELECT master_data_version FROM table_session_billing_policy WHERE table_session_id = 'TS-1';"));
        Assert.Equal("MASTER-2", await database.ScalarAsync<string>(
            "SELECT master_data_version FROM table_session_billing_policy WHERE table_session_id = 'TS-3';"));
        Assert.Equal("8", await database.ScalarAsync<string>(
            "SELECT vat_rate FROM table_session_billing_policy WHERE table_session_id = 'TS-3';"));
        Assert.Equal(2L, await database.CountAsync("table_session_billing_policy"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM master_data_packages WHERE branch_id = 'BRANCH-01' AND lifecycle_status = 'Active';"));
        Assert.Equal("MASTER-3", await database.ScalarAsync<string>(
            "SELECT master_data_version FROM master_data_packages WHERE branch_id = 'BRANCH-01' AND lifecycle_status = 'Active';"));
        Assert.Equal(2L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM master_data_packages WHERE branch_id = 'BRANCH-01' AND lifecycle_status = 'Previous';"));
    }

    [Fact]
    public async Task Active_package_without_billing_policy_rolls_back_open_table()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync(seedActiveMasterData: false);
        await database.ExecuteAsync($$"""
            INSERT INTO master_data_packages (
                branch_id, master_data_version, package_schema_version, checksum_sha256,
                published_at, activated_at, lifecycle_status)
            VALUES ('BRANCH-01', 'MASTER-BROKEN', 1, '{{new string('B', 64)}}',
                    '2026-10-13T07:00:00.0000000+00:00',
                    '2026-10-13T08:00:00.0000000+00:00', 'Active');
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04")));

        Assert.Equal("The active Master Data package has no billing policy.", exception.Message);
        Assert.Equal(0L, await database.CountAsync("table_sessions"));
        Assert.Equal(0L, await database.CountAsync("table_session_billing_policy"));
        Assert.Equal(0L, await database.CountAsync("command_receipts"));
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
                var masterData = await transaction.FindActiveMasterDataSnapshotAsync("BRANCH-01", token);
                Assert.NotNull(masterData);
                await transaction.AddTableSessionBillingPolicyAsync(
                    "BRANCH-01",
                    session.Id,
                    masterData,
                    Now,
                    token);
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
        Assert.Equal(0L, await database.CountAsync("table_session_billing_policy"));
    }

    [Fact]
    public async Task Version_one_database_migrates_to_current_version_without_losing_sessions()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        await Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await database.ExecuteAsync("""
            DROP TABLE master_data_billing_policy;
            DROP TABLE master_data_packages;
            DROP TABLE order_line_options;
            DROP TABLE order_lines;
            DROP TABLE order_batches;
            DROP TABLE table_session_billing_policy;
            DROP TABLE print_jobs;
            DROP TABLE bill_preview_snapshots;
            PRAGMA user_version = 1;
            """);

        var migratedStore = new SqliteTableSessionCommandStore(
            database.Path,
            new StaticBillPreviewContentSource());
        await migratedStore.InitializeAsync();

        Assert.Equal(5L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.CountAsync("table_sessions"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'bill_preview_snapshots';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'print_jobs';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'order_lines';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'master_data_packages';"));
    }

    [Fact]
    public async Task Version_two_database_migrates_to_current_version_without_losing_preview_jobs()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        await Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await Service(store).RequestPaymentAsync(PaymentCommand("REQ-2", "TS-1", 1));
        await database.ExecuteAsync("""
            DROP TABLE master_data_billing_policy;
            DROP TABLE master_data_packages;
            DROP TABLE order_line_options;
            DROP TABLE order_lines;
            DROP TABLE order_batches;
            DROP TABLE table_session_billing_policy;
            PRAGMA user_version = 2;
            """);

        var migratedStore = new SqliteTableSessionCommandStore(
            database.Path,
            new StaticBillPreviewContentSource());
        await migratedStore.InitializeAsync();

        Assert.Equal(5L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.CountAsync("bill_preview_snapshots"));
        Assert.Equal(1L, await database.CountAsync("print_jobs"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'table_session_billing_policy';"));
    }

    [Fact]
    public async Task Version_three_database_migrates_to_current_without_losing_session_policy()
    {
        await using var database = new TemporaryDatabase();
        var store = await database.CreateStoreAsync();
        await Service(store).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await database.ExecuteAsync("""
            DROP TABLE master_data_billing_policy;
            DROP TABLE master_data_packages;
            PRAGMA user_version = 3;
            """);

        var migratedStore = new SqliteTableSessionCommandStore(
            database.Path,
            new StaticBillPreviewContentSource());
        await migratedStore.InitializeAsync();

        Assert.Equal(5L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal("MASTER-1", await database.ScalarAsync<string>(
            "SELECT master_data_version FROM table_session_billing_policy WHERE table_session_id = 'TS-1';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'master_data_packages';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'master_data_billing_policy';"));
    }

    [Fact]
    public async Task Version_four_database_migrates_to_version_five_without_replacing_active_package()
    {
        await using var database = new TemporaryDatabase();
        await database.CreateStoreAsync();
        await database.ExecuteAsync("""
            DROP TABLE branch_sold_out_audit;
            DROP TABLE branch_sold_out_overrides;
            DROP TABLE master_data_option_group_items;
            DROP TABLE master_data_option_groups;
            DROP TABLE master_data_sellable_items;
            PRAGMA user_version = 4;
            """);

        var migratedStore = new SqliteTableSessionCommandStore(
            database.Path,
            new StaticBillPreviewContentSource());
        await migratedStore.InitializeAsync();

        Assert.Equal(5L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal("MASTER-1", await database.ScalarAsync<string>("""
            SELECT master_data_version FROM master_data_packages
            WHERE branch_id = 'BRANCH-01' AND lifecycle_status = 'Active';
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'master_data_sellable_items';"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'branch_sold_out_overrides';"));
    }

    [Fact]
    public async Task Production_preview_source_uses_persisted_snapshots_and_authoritative_calculation()
    {
        await using var database = new TemporaryDatabase();
        var setupStore = await database.CreateStoreAsync();
        await database.ActivateMasterDataAsync(
            "BRANCH-01",
            "MASTER-7",
            "CALC-3",
            vatRate: 7m,
            serviceChargeRate: 10m);
        await Service(setupStore).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await database.ExecuteAsync("""
            UPDATE table_session_billing_policy
            SET bill_discount_kind = 'FixedAmount',
                bill_discount_requested_value = '20'
            WHERE branch_id = 'BRANCH-01' AND table_session_id = 'TS-1';

            INSERT INTO order_batches (
                branch_id, order_batch_id, request_id, table_session_id,
                device_id, employee_id, submitted_at)
            VALUES ('BRANCH-01', 'BATCH-1', 'ORDER-REQ-1', 'TS-1',
                    'DEVICE-01', 'EMPLOYEE-01', '2026-10-13T08:01:00.0000000+00:00');

            INSERT INTO order_lines (
                branch_id, order_line_id, order_batch_id, table_session_id, line_sequence,
                sellable_item_id, display_name, original_quantity, active_quantity, unit_price,
                fulfillment_type, comment, discount_kind, discount_requested_value, status, created_at)
            VALUES ('BRANCH-01', 'LINE-1', 'BATCH-1', 'TS-1', 1,
                    'ITEM-1', 'ข้าวผัดกุ้ง', 2, 2, '100', 'DineIn', 'ไม่ใส่ต้นหอม',
                    'Percentage', '10', 'Active', '2026-10-13T08:01:00.0000000+00:00');

            INSERT INTO order_line_options (
                branch_id, order_line_id, option_sequence, option_id,
                display_name, quantity_per_item, unit_price)
            VALUES ('BRANCH-01', 'LINE-1', 1, 'OPT-1', 'ไข่ดาว', 1, '15');
            """);
        var productionStore = new SqliteTableSessionCommandStore(database.Path);
        await productionStore.InitializeAsync();

        var result = await Service(productionStore).RequestPaymentAsync(
            PaymentCommand("REQ-2", "TS-1", 1));

        Assert.Equal(BranchCommandOutcome.BranchAccepted, result.Outcome);
        var json = await database.ScalarAsync<string>(
            "SELECT content_json FROM bill_preview_snapshots WHERE request_id = 'REQ-2';");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("MASTER-7", root.GetProperty("masterDataVersion").GetString());
        Assert.Equal("CALC-3", root.GetProperty("calculationPolicyVersion").GetString());
        Assert.Equal(230m, root.GetProperty("itemGrossAmount").GetDecimal());
        Assert.Equal(23m, root.GetProperty("lineDiscountAmount").GetDecimal());
        Assert.Equal(205.70m, root.GetProperty("totalBeforeRounding").GetDecimal());
        Assert.Equal(-0.70m, root.GetProperty("roundingAdjustment").GetDecimal());
        Assert.Equal(205m, root.GetProperty("netTotal").GetDecimal());
        Assert.Equal("BATCH-1", root.GetProperty("items")[0].GetProperty("orderBatchId").GetString());
        Assert.Equal("ไข่ดาว", root.GetProperty("items")[0].GetProperty("options")[0]
            .GetProperty("displayName").GetString());
        Assert.Equal(10m, root.GetProperty("items")[0].GetProperty("discount")
            .GetProperty("requestedValue").GetDecimal());
        Assert.Equal(23m, root.GetProperty("items")[0].GetProperty("discount")
            .GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(20m, root.GetProperty("billDiscount").GetProperty("appliedAmount").GetDecimal());
    }

    [Fact]
    public async Task Production_preview_source_rejects_session_without_billing_policy_and_rolls_back()
    {
        await using var database = new TemporaryDatabase();
        var setupStore = await database.CreateStoreAsync();
        await Service(setupStore).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        await database.ExecuteAsync("""
            DELETE FROM table_session_billing_policy
            WHERE branch_id = 'BRANCH-01' AND table_session_id = 'TS-1';

            INSERT INTO order_batches (
                branch_id, order_batch_id, request_id, table_session_id,
                device_id, employee_id, submitted_at)
            VALUES ('BRANCH-01', 'BATCH-1', 'ORDER-REQ-1', 'TS-1',
                    'DEVICE-01', 'EMPLOYEE-01', '2026-10-13T08:01:00.0000000+00:00');

            INSERT INTO order_lines (
                branch_id, order_line_id, order_batch_id, table_session_id, line_sequence,
                sellable_item_id, display_name, original_quantity, active_quantity, unit_price,
                fulfillment_type, comment, discount_kind, discount_requested_value, status, created_at)
            VALUES ('BRANCH-01', 'LINE-1', 'BATCH-1', 'TS-1', 1,
                    'ITEM-1', 'Item', 1, 1, '100', 'DineIn', NULL,
                    NULL, NULL, 'Active', '2026-10-13T08:01:00.0000000+00:00');
            """);
        var productionStore = new SqliteTableSessionCommandStore(database.Path);
        await productionStore.InitializeAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(productionStore).RequestPaymentAsync(PaymentCommand("REQ-2", "TS-1", 1)));

        Assert.Equal("The table session has no authoritative billing policy snapshot.", exception.Message);
        Assert.Equal("Ordering", await database.ScalarAsync<string>(
            "SELECT status FROM table_sessions WHERE table_session_id = 'TS-1';"));
        Assert.Equal(0L, await database.CountAsync("bill_preview_snapshots"));
        Assert.Equal(0L, await database.CountAsync("print_jobs"));
    }

    [Fact]
    public async Task Production_preview_source_rejects_session_without_active_order_lines()
    {
        await using var database = new TemporaryDatabase();
        var setupStore = await database.CreateStoreAsync();
        await Service(setupStore).OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        var productionStore = new SqliteTableSessionCommandStore(database.Path);
        await productionStore.InitializeAsync();

        var result = await Service(productionStore).RequestPaymentAsync(
            PaymentCommand("REQ-2", "TS-1", 1));

        Assert.Equal(BranchCommandOutcome.Rejected, result.Outcome);
        Assert.Equal("NoActiveOrderLines", result.ErrorCode);
        Assert.Equal("Ordering", await database.ScalarAsync<string>(
            "SELECT status FROM table_sessions WHERE table_session_id = 'TS-1';"));
        Assert.Equal(0L, await database.CountAsync("bill_preview_snapshots"));
        Assert.Equal(0L, await database.CountAsync("print_jobs"));
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
        await database.ExecuteAsync("PRAGMA user_version = 6;");
        var store = new SqliteTableSessionCommandStore(database.Path, new StaticBillPreviewContentSource());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.InitializeAsync());

        Assert.Contains("schema version 6", exception.Message, StringComparison.Ordinal);
        Assert.Equal(6L, await database.ScalarAsync<long>("PRAGMA user_version;"));
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
            ISqliteBillPreviewContentSource? contentSource = null,
            bool seedActiveMasterData = true)
        {
            var store = new SqliteTableSessionCommandStore(
                Path,
                contentSource ?? new StaticBillPreviewContentSource());
            await store.InitializeAsync();
            if (seedActiveMasterData)
            {
                await SeedActiveMasterDataAsync("BRANCH-01");
                await SeedActiveMasterDataAsync("BRANCH-02");
            }

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
                "print_jobs",
                "table_session_billing_policy",
                "order_batches",
                "order_lines",
                "order_line_options",
                "master_data_packages",
                "master_data_billing_policy"
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

        public Task SeedActiveMasterDataAsync(
            string branchId,
            string masterDataVersion = "MASTER-1",
            string calculationPolicyVersion = "CALC-1",
            decimal vatRate = 7m,
            decimal serviceChargeRate = 0m)
        {
            var escapedBranchId = SqlLiteral(branchId);
            var escapedMasterDataVersion = SqlLiteral(masterDataVersion);
            var escapedCalculationPolicyVersion = SqlLiteral(calculationPolicyVersion);
            return ExecuteAsync($$"""
                INSERT INTO master_data_packages (
                    branch_id, master_data_version, package_schema_version, checksum_sha256,
                    published_at, activated_at, lifecycle_status)
                SELECT '{{escapedBranchId}}', '{{escapedMasterDataVersion}}', 1,
                       '{{new string('A', 64)}}',
                       '2026-10-13T07:00:00.0000000+00:00',
                       '2026-10-13T08:00:00.0000000+00:00', 'Active'
                WHERE NOT EXISTS (
                    SELECT 1 FROM master_data_packages
                    WHERE branch_id = '{{escapedBranchId}}' AND lifecycle_status = 'Active');

                INSERT OR IGNORE INTO master_data_billing_policy (
                    branch_id, master_data_version, calculation_policy_version,
                    vat_rate, service_charge_rate, currency_scale, midpoint_rounding)
                SELECT branch_id, master_data_version, '{{escapedCalculationPolicyVersion}}',
                       '{{vatRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}',
                       '{{serviceChargeRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}',
                       2, 'AwayFromZero'
                FROM master_data_packages
                WHERE branch_id = '{{escapedBranchId}}'
                  AND master_data_version = '{{escapedMasterDataVersion}}';
                """);
        }

        public async Task ActivateMasterDataAsync(
            string branchId,
            string masterDataVersion,
            string calculationPolicyVersion,
            decimal vatRate,
            decimal serviceChargeRate)
        {
            var escapedBranchId = SqlLiteral(branchId);
            await ExecuteAsync($$"""
                UPDATE master_data_packages
                SET lifecycle_status = 'Previous'
                WHERE branch_id = '{{escapedBranchId}}' AND lifecycle_status = 'Active';
                """);
            await SeedActiveMasterDataAsync(
                branchId,
                masterDataVersion,
                calculationPolicyVersion,
                vatRate,
                serviceChargeRate);
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

        private static string SqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }

    private sealed class StaticBillPreviewContentSource : ISqliteBillPreviewContentSource
    {
        public Task<bool> HasActiveOrderLinesAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string branchId,
            string tableSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

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
        public Task<bool> HasActiveOrderLinesAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string branchId,
            string tableSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

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
