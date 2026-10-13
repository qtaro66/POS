using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

internal static class SqliteBranchSchema
{
    public const int CurrentVersion = 5;

    private const string SchemaVersion1 = """
        CREATE TABLE IF NOT EXISTS table_sessions (
            branch_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            table_id TEXT NOT NULL,
            status TEXT NOT NULL CHECK (status IN (
                'Ordering', 'PaymentPending', 'PaymentInProgress',
                'PartiallyPaid', 'Completed', 'Cancelled', 'Closed')),
            version INTEGER NOT NULL CHECK (version >= 1),
            PRIMARY KEY (branch_id, table_session_id)
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_table_sessions_active_table
            ON table_sessions (branch_id, table_id)
            WHERE status IN ('Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid');

        CREATE TABLE IF NOT EXISTS command_receipts (
            branch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            device_id TEXT NOT NULL,
            employee_id TEXT NOT NULL,
            client_created_at TEXT NOT NULL,
            outcome TEXT NOT NULL CHECK (outcome IN ('BranchAccepted', 'Rejected')),
            table_session_id TEXT NOT NULL,
            session_status TEXT NULL,
            table_version INTEGER NULL,
            error_code TEXT NULL,
            server_received_at TEXT NOT NULL,
            PRIMARY KEY (branch_id, request_id)
        );

        CREATE TABLE IF NOT EXISTS table_session_audit (
            audit_id TEXT NOT NULL PRIMARY KEY,
            branch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            action TEXT NOT NULL,
            previous_status TEXT NULL,
            current_status TEXT NOT NULL,
            previous_table_id TEXT NOT NULL,
            current_table_id TEXT NOT NULL,
            table_version INTEGER NOT NULL,
            employee_id TEXT NOT NULL,
            device_id TEXT NOT NULL,
            occurred_at TEXT NOT NULL,
            reason TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_table_session_audit_session
            ON table_session_audit (branch_id, table_session_id, table_version);

        CREATE TABLE IF NOT EXISTS cloud_sync_outbox (
            outbox_id TEXT NOT NULL PRIMARY KEY,
            branch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            action TEXT NOT NULL,
            table_version INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            processing_status TEXT NOT NULL DEFAULT 'Pending',
            retry_count INTEGER NOT NULL DEFAULT 0 CHECK (retry_count >= 0),
            next_retry_at TEXT NULL,
            last_error TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_cloud_sync_outbox_pending
            ON cloud_sync_outbox (processing_status, next_retry_at, created_at);

        CREATE TABLE IF NOT EXISTS cancellation_requests (
            branch_id TEXT NOT NULL,
            cancellation_request_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            status TEXT NOT NULL,
            PRIMARY KEY (branch_id, cancellation_request_id)
        );
        """;

    private const string SchemaVersion2 = """
        CREATE TABLE IF NOT EXISTS bill_preview_snapshots (
            snapshot_id TEXT NOT NULL PRIMARY KEY,
            branch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            table_id TEXT NOT NULL,
            document_label TEXT NOT NULL,
            content_schema_version INTEGER NOT NULL CHECK (content_schema_version >= 1),
            content_json TEXT NOT NULL,
            content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
            created_at TEXT NOT NULL,
            UNIQUE (branch_id, request_id)
        );

        CREATE TABLE IF NOT EXISTS print_jobs (
            print_job_id TEXT NOT NULL PRIMARY KEY,
            branch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            snapshot_id TEXT NOT NULL,
            purpose TEXT NOT NULL CHECK (purpose IN ('BillPreview')),
            target_station TEXT NOT NULL CHECK (target_station IN ('Cashier')),
            status TEXT NOT NULL CHECK (status IN (
                'PrintQueued', 'Dispatching', 'PrintDispatched', 'Failed', 'Unknown', 'Completed')),
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
            next_retry_at TEXT NULL,
            last_error TEXT NULL,
            blocked_reason TEXT NULL,
            created_at TEXT NOT NULL,
            original_print_job_id TEXT NULL,
            is_reprint INTEGER NOT NULL DEFAULT 0 CHECK (is_reprint IN (0, 1)),
            FOREIGN KEY (snapshot_id) REFERENCES bill_preview_snapshots (snapshot_id),
            FOREIGN KEY (original_print_job_id) REFERENCES print_jobs (print_job_id),
            UNIQUE (branch_id, request_id, purpose, target_station)
        );

        CREATE INDEX IF NOT EXISTS ix_print_jobs_dispatch
            ON print_jobs (status, next_retry_at, created_at);
        """;

    private const string SchemaVersion3 = """
        CREATE TABLE IF NOT EXISTS table_session_billing_policy (
            branch_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            master_data_version TEXT NOT NULL,
            calculation_policy_version TEXT NOT NULL,
            vat_rate TEXT NOT NULL,
            service_charge_rate TEXT NOT NULL,
            currency_scale INTEGER NOT NULL CHECK (currency_scale BETWEEN 0 AND 28),
            midpoint_rounding TEXT NOT NULL CHECK (midpoint_rounding IN (
                'AwayFromZero', 'ToEven', 'ToZero', 'ToNegativeInfinity', 'ToPositiveInfinity')),
            bill_discount_kind TEXT NULL CHECK (bill_discount_kind IS NULL OR bill_discount_kind IN ('Percentage', 'FixedAmount')),
            bill_discount_requested_value TEXT NULL,
            created_at TEXT NOT NULL,
            CHECK ((bill_discount_kind IS NULL) = (bill_discount_requested_value IS NULL)),
            PRIMARY KEY (branch_id, table_session_id),
            FOREIGN KEY (branch_id, table_session_id)
                REFERENCES table_sessions (branch_id, table_session_id)
        );

        CREATE TABLE IF NOT EXISTS order_batches (
            branch_id TEXT NOT NULL,
            order_batch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            device_id TEXT NOT NULL,
            employee_id TEXT NOT NULL,
            submitted_at TEXT NOT NULL,
            PRIMARY KEY (branch_id, order_batch_id),
            UNIQUE (branch_id, request_id),
            FOREIGN KEY (branch_id, table_session_id)
                REFERENCES table_sessions (branch_id, table_session_id)
        );

        CREATE INDEX IF NOT EXISTS ix_order_batches_session
            ON order_batches (branch_id, table_session_id, submitted_at, order_batch_id);

        CREATE TABLE IF NOT EXISTS order_lines (
            branch_id TEXT NOT NULL,
            order_line_id TEXT NOT NULL,
            order_batch_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            line_sequence INTEGER NOT NULL CHECK (line_sequence >= 1),
            sellable_item_id TEXT NOT NULL,
            display_name TEXT NOT NULL,
            original_quantity INTEGER NOT NULL CHECK (original_quantity >= 1),
            active_quantity INTEGER NOT NULL CHECK (active_quantity BETWEEN 0 AND original_quantity),
            unit_price TEXT NOT NULL,
            fulfillment_type TEXT NOT NULL CHECK (fulfillment_type IN ('DineIn', 'Takeaway')),
            comment TEXT NULL,
            discount_kind TEXT NULL CHECK (discount_kind IS NULL OR discount_kind IN ('Percentage', 'FixedAmount')),
            discount_requested_value TEXT NULL,
            status TEXT NOT NULL CHECK (status IN ('Active', 'Cancelled')),
            created_at TEXT NOT NULL,
            CHECK ((discount_kind IS NULL) = (discount_requested_value IS NULL)),
            PRIMARY KEY (branch_id, order_line_id),
            UNIQUE (branch_id, order_batch_id, line_sequence),
            FOREIGN KEY (branch_id, order_batch_id)
                REFERENCES order_batches (branch_id, order_batch_id),
            FOREIGN KEY (branch_id, table_session_id)
                REFERENCES table_sessions (branch_id, table_session_id)
        );

        CREATE INDEX IF NOT EXISTS ix_order_lines_active_session
            ON order_lines (branch_id, table_session_id, status, order_batch_id, line_sequence);

        CREATE TABLE IF NOT EXISTS order_line_options (
            branch_id TEXT NOT NULL,
            order_line_id TEXT NOT NULL,
            option_sequence INTEGER NOT NULL CHECK (option_sequence >= 1),
            option_id TEXT NOT NULL,
            display_name TEXT NOT NULL,
            quantity_per_item INTEGER NOT NULL CHECK (quantity_per_item >= 1),
            unit_price TEXT NOT NULL,
            PRIMARY KEY (branch_id, order_line_id, option_sequence),
            FOREIGN KEY (branch_id, order_line_id)
                REFERENCES order_lines (branch_id, order_line_id)
        );
        """;

    private const string SchemaVersion4 = """
        CREATE TABLE IF NOT EXISTS master_data_packages (
            branch_id TEXT NOT NULL,
            master_data_version TEXT NOT NULL,
            package_schema_version INTEGER NOT NULL CHECK (package_schema_version >= 1),
            checksum_sha256 TEXT NOT NULL CHECK (
                length(checksum_sha256) = 64
                AND checksum_sha256 NOT GLOB '*[^0-9A-Fa-f]*'),
            published_at TEXT NOT NULL,
            activated_at TEXT NULL,
            lifecycle_status TEXT NOT NULL CHECK (lifecycle_status IN ('Staged', 'Active', 'Previous')),
            CHECK (lifecycle_status = 'Staged' OR activated_at IS NOT NULL),
            PRIMARY KEY (branch_id, master_data_version)
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_master_data_packages_active
            ON master_data_packages (branch_id)
            WHERE lifecycle_status = 'Active';

        CREATE INDEX IF NOT EXISTS ix_master_data_packages_lifecycle
            ON master_data_packages (branch_id, lifecycle_status);

        CREATE TABLE IF NOT EXISTS master_data_billing_policy (
            branch_id TEXT NOT NULL,
            master_data_version TEXT NOT NULL,
            calculation_policy_version TEXT NOT NULL,
            vat_rate TEXT NOT NULL,
            service_charge_rate TEXT NOT NULL,
            currency_scale INTEGER NOT NULL CHECK (currency_scale BETWEEN 0 AND 28),
            midpoint_rounding TEXT NOT NULL CHECK (midpoint_rounding IN (
                'AwayFromZero', 'ToEven', 'ToZero', 'ToNegativeInfinity', 'ToPositiveInfinity')),
            PRIMARY KEY (branch_id, master_data_version),
            FOREIGN KEY (branch_id, master_data_version)
                REFERENCES master_data_packages (branch_id, master_data_version)
        );
        """;

    private const string SchemaVersion5 = """
        CREATE TABLE IF NOT EXISTS master_data_sellable_items (
            branch_id TEXT NOT NULL,
            master_data_version TEXT NOT NULL,
            sellable_item_id TEXT NOT NULL,
            item_kind TEXT NOT NULL CHECK (item_kind IN ('Item', 'SubItem', 'Topping')),
            display_name TEXT NOT NULL CHECK (length(trim(display_name)) > 0),
            resolved_price TEXT NULL,
            central_active INTEGER NOT NULL CHECK (central_active IN (0, 1)),
            branch_catalog_enabled INTEGER NOT NULL CHECK (branch_catalog_enabled IN (0, 1)),
            PRIMARY KEY (branch_id, master_data_version, sellable_item_id),
            FOREIGN KEY (branch_id, master_data_version)
                REFERENCES master_data_packages (branch_id, master_data_version)
        );

        CREATE TABLE IF NOT EXISTS master_data_option_groups (
            branch_id TEXT NOT NULL,
            master_data_version TEXT NOT NULL,
            parent_sellable_item_id TEXT NOT NULL,
            option_group_id TEXT NOT NULL,
            display_name TEXT NOT NULL CHECK (length(trim(display_name)) > 0),
            option_type TEXT NOT NULL CHECK (option_type IN ('SubItem', 'Topping')),
            min_selections INTEGER NOT NULL CHECK (min_selections >= 0),
            max_selections INTEGER NOT NULL CHECK (max_selections >= min_selections),
            allow_repeated_option INTEGER NOT NULL CHECK (allow_repeated_option IN (0, 1)),
            max_quantity_per_option INTEGER NOT NULL CHECK (max_quantity_per_option >= 1),
            display_order INTEGER NOT NULL CHECK (display_order >= 0),
            PRIMARY KEY (branch_id, master_data_version, parent_sellable_item_id, option_group_id),
            FOREIGN KEY (branch_id, master_data_version, parent_sellable_item_id)
                REFERENCES master_data_sellable_items (
                    branch_id, master_data_version, sellable_item_id)
        );

        CREATE TABLE IF NOT EXISTS master_data_option_group_items (
            branch_id TEXT NOT NULL,
            master_data_version TEXT NOT NULL,
            parent_sellable_item_id TEXT NOT NULL,
            option_group_id TEXT NOT NULL,
            option_sellable_item_id TEXT NOT NULL,
            display_order INTEGER NOT NULL CHECK (display_order >= 0),
            is_default INTEGER NOT NULL DEFAULT 0 CHECK (is_default IN (0, 1)),
            PRIMARY KEY (
                branch_id, master_data_version, parent_sellable_item_id,
                option_group_id, option_sellable_item_id),
            FOREIGN KEY (
                branch_id, master_data_version, parent_sellable_item_id, option_group_id)
                REFERENCES master_data_option_groups (
                    branch_id, master_data_version, parent_sellable_item_id, option_group_id),
            FOREIGN KEY (branch_id, master_data_version, option_sellable_item_id)
                REFERENCES master_data_sellable_items (
                    branch_id, master_data_version, sellable_item_id)
        );

        CREATE TABLE IF NOT EXISTS branch_sold_out_overrides (
            branch_id TEXT NOT NULL,
            sellable_item_id TEXT NOT NULL,
            is_sold_out INTEGER NOT NULL CHECK (is_sold_out IN (0, 1)),
            changed_by_employee_id TEXT NOT NULL,
            changed_by_device_id TEXT NOT NULL,
            changed_at TEXT NOT NULL,
            PRIMARY KEY (branch_id, sellable_item_id)
        );

        CREATE TABLE IF NOT EXISTS branch_sold_out_audit (
            audit_id TEXT NOT NULL PRIMARY KEY,
            branch_id TEXT NOT NULL,
            sellable_item_id TEXT NOT NULL,
            previous_sold_out INTEGER NOT NULL CHECK (previous_sold_out IN (0, 1)),
            current_sold_out INTEGER NOT NULL CHECK (current_sold_out IN (0, 1)),
            employee_id TEXT NOT NULL,
            device_id TEXT NOT NULL,
            occurred_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_branch_sold_out_audit_item
            ON branch_sold_out_audit (branch_id, sellable_item_id, occurred_at);

        CREATE INDEX IF NOT EXISTS ix_master_data_option_groups_parent
            ON master_data_option_groups (
                branch_id, master_data_version, parent_sellable_item_id, display_order);
        """;

    public static async Task InitializeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var settingsCommand = connection.CreateCommand();
        settingsCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;";
        await settingsCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction(deferred: false);
        var storedVersion = await ReadVersionAsync(connection, transaction, cancellationToken);
        if (storedVersion > CurrentVersion || storedVersion < 0)
        {
            throw new InvalidOperationException(
                $"Unsupported branch database schema version {storedVersion}; expected at most {CurrentVersion}.");
        }

        if (storedVersion < 1)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion1, cancellationToken);
        }

        if (storedVersion < 2)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion2, cancellationToken);
        }

        if (storedVersion < 3)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion3, cancellationToken);
        }

        if (storedVersion < 4)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion4, cancellationToken);
        }

        if (storedVersion < 5)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion5, cancellationToken);
        }

        if (storedVersion != CurrentVersion)
        {
            await ExecuteAsync(
                connection,
                transaction,
                $"PRAGMA user_version = {CurrentVersion};",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> ReadVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
