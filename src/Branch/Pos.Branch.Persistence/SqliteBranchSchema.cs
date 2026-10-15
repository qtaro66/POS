using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

internal static class SqliteBranchSchema
{
    public const int CurrentVersion = 10;

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

    private const string SchemaVersion6 = """
        ALTER TABLE command_receipts ADD COLUMN error_details_json TEXT NULL;
        ALTER TABLE command_receipts ADD COLUMN branch_sequence INTEGER NULL CHECK (branch_sequence >= 1);
        ALTER TABLE order_line_options ADD COLUMN option_group_id TEXT NULL;
        ALTER TABLE order_batches ADD COLUMN branch_sequence INTEGER NULL CHECK (branch_sequence >= 1);
        ALTER TABLE order_batches ADD COLUMN client_created_at TEXT NULL;
        ALTER TABLE order_batches ADD COLUMN server_received_at TEXT NULL;

        WITH ranked AS (
            SELECT rowid AS batch_rowid,
                   ROW_NUMBER() OVER (
                       PARTITION BY branch_id
                       ORDER BY submitted_at, order_batch_id) AS sequence
            FROM order_batches
        )
        UPDATE order_batches
        SET branch_sequence = (
                SELECT sequence FROM ranked
                WHERE ranked.batch_rowid = order_batches.rowid),
            client_created_at = COALESCE(
                (SELECT receipt.client_created_at
                 FROM command_receipts AS receipt
                 WHERE receipt.branch_id = order_batches.branch_id
                   AND receipt.request_id = order_batches.request_id),
                submitted_at),
            server_received_at = submitted_at;

        UPDATE command_receipts
        SET branch_sequence = (
            SELECT batch.branch_sequence
            FROM order_batches AS batch
            WHERE batch.branch_id = command_receipts.branch_id
              AND batch.request_id = command_receipts.request_id)
        WHERE EXISTS (
            SELECT 1 FROM order_batches AS batch
            WHERE batch.branch_id = command_receipts.branch_id
              AND batch.request_id = command_receipts.request_id);

        CREATE UNIQUE INDEX IF NOT EXISTS ux_order_batches_branch_sequence
            ON order_batches (branch_id, branch_sequence)
            WHERE branch_sequence IS NOT NULL;
        """;

    private const string SchemaVersion7ProductionSnapshots = """
        CREATE TABLE IF NOT EXISTS production_order_snapshots (
            snapshot_id TEXT NOT NULL PRIMARY KEY,
            branch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            order_batch_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            table_id TEXT NOT NULL,
            production_station_id TEXT NOT NULL CHECK (
                length(trim(production_station_id)) > 0),
            content_schema_version INTEGER NOT NULL CHECK (content_schema_version >= 1),
            content_json TEXT NOT NULL,
            content_sha256 TEXT NOT NULL CHECK (
                length(content_sha256) = 64
                AND content_sha256 NOT GLOB '*[^0-9A-Fa-f]*'),
            created_at TEXT NOT NULL,
            UNIQUE (branch_id, order_batch_id, production_station_id),
            FOREIGN KEY (branch_id, order_batch_id)
                REFERENCES order_batches (branch_id, order_batch_id),
            FOREIGN KEY (branch_id, table_session_id)
                REFERENCES table_sessions (branch_id, table_session_id)
        );

        CREATE INDEX IF NOT EXISTS ix_production_order_snapshots_session
            ON production_order_snapshots (
                branch_id, table_session_id, created_at, snapshot_id);
        """;

    private const string SchemaVersion7PrintJobs = """
        CREATE TABLE print_jobs_v7 (
            print_job_id TEXT NOT NULL PRIMARY KEY,
            branch_id TEXT NOT NULL,
            request_id TEXT NOT NULL,
            table_session_id TEXT NOT NULL,
            snapshot_id TEXT NOT NULL,
            purpose TEXT NOT NULL CHECK (purpose IN ('BillPreview', 'KitchenOrder')),
            target_station TEXT NOT NULL CHECK (length(trim(target_station)) > 0),
            status TEXT NOT NULL CHECK (status IN (
                'PrintQueued', 'Dispatching', 'PrintDispatched', 'Failed', 'Unknown', 'Completed')),
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
            next_retry_at TEXT NULL,
            last_error TEXT NULL,
            blocked_reason TEXT NULL,
            created_at TEXT NOT NULL,
            original_print_job_id TEXT NULL,
            is_reprint INTEGER NOT NULL DEFAULT 0 CHECK (is_reprint IN (0, 1)),
            FOREIGN KEY (original_print_job_id) REFERENCES print_jobs_v7 (print_job_id),
            UNIQUE (branch_id, request_id, purpose, target_station)
        );

        INSERT INTO print_jobs_v7 (
            print_job_id, branch_id, request_id, table_session_id, snapshot_id,
            purpose, target_station, status, attempt_count, next_retry_at,
            last_error, blocked_reason, created_at, original_print_job_id, is_reprint)
        SELECT print_job_id, branch_id, request_id, table_session_id, snapshot_id,
               purpose, target_station, status, attempt_count, next_retry_at,
               last_error, blocked_reason, created_at, original_print_job_id, is_reprint
        FROM print_jobs;

        DROP TABLE print_jobs;
        ALTER TABLE print_jobs_v7 RENAME TO print_jobs;

        CREATE INDEX ix_print_jobs_dispatch
            ON print_jobs (status, next_retry_at, created_at);
        """;

    private const string SchemaVersion7PrintJobGuards = """
        CREATE TRIGGER IF NOT EXISTS print_jobs_snapshot_insert_guard
        BEFORE INSERT ON print_jobs
        BEGIN
            SELECT CASE
                WHEN NEW.purpose = 'BillPreview' AND NOT EXISTS (
                    SELECT 1 FROM bill_preview_snapshots
                    WHERE snapshot_id = NEW.snapshot_id
                      AND branch_id = NEW.branch_id
                      AND request_id = NEW.request_id
                      AND table_session_id = NEW.table_session_id)
                THEN RAISE(ABORT, 'Bill Preview snapshot not found')
                WHEN NEW.purpose = 'KitchenOrder' AND NOT EXISTS (
                    SELECT 1 FROM production_order_snapshots
                    WHERE snapshot_id = NEW.snapshot_id
                      AND branch_id = NEW.branch_id
                      AND request_id = NEW.request_id
                      AND table_session_id = NEW.table_session_id
                      AND production_station_id = NEW.target_station)
                THEN RAISE(ABORT, 'Production order snapshot not found or station mismatch')
            END;
        END;

        CREATE TRIGGER IF NOT EXISTS print_jobs_snapshot_update_guard
        BEFORE UPDATE OF snapshot_id, purpose, target_station,
                         branch_id, request_id, table_session_id ON print_jobs
        BEGIN
            SELECT CASE
                WHEN NEW.purpose = 'BillPreview' AND NOT EXISTS (
                    SELECT 1 FROM bill_preview_snapshots
                    WHERE snapshot_id = NEW.snapshot_id
                      AND branch_id = NEW.branch_id
                      AND request_id = NEW.request_id
                      AND table_session_id = NEW.table_session_id)
                THEN RAISE(ABORT, 'Bill Preview snapshot not found')
                WHEN NEW.purpose = 'KitchenOrder' AND NOT EXISTS (
                    SELECT 1 FROM production_order_snapshots
                    WHERE snapshot_id = NEW.snapshot_id
                      AND branch_id = NEW.branch_id
                      AND request_id = NEW.request_id
                      AND table_session_id = NEW.table_session_id
                      AND production_station_id = NEW.target_station)
                THEN RAISE(ABORT, 'Production order snapshot not found or station mismatch')
            END;
        END;
        """;

    private const string SchemaVersion8 = """
        CREATE TABLE IF NOT EXISTS printer_profiles (
            printer_id TEXT NOT NULL PRIMARY KEY CHECK (length(trim(printer_id)) > 0),
            branch_id TEXT NOT NULL CHECK (length(trim(branch_id)) > 0),
            display_name TEXT NOT NULL CHECK (length(trim(display_name)) > 0),
            production_station_id TEXT NOT NULL CHECK (
                length(trim(production_station_id)) > 0),
            host TEXT NOT NULL CHECK (length(trim(host)) > 0),
            port INTEGER NOT NULL CHECK (port BETWEEN 1 AND 65535),
            protocol TEXT NOT NULL CHECK (protocol IN ('RawTcpEscPos', 'Ipp', 'Lpr')),
            compatibility_profile_id TEXT NOT NULL CHECK (
                length(trim(compatibility_profile_id)) > 0),
            manufacturer TEXT NOT NULL CHECK (length(trim(manufacturer)) > 0),
            model TEXT NOT NULL CHECK (length(trim(model)) > 0),
            product_code TEXT NULL,
            firmware_version TEXT NULL,
            paper_width_millimeters INTEGER NOT NULL CHECK (paper_width_millimeters >= 1),
            dpi INTEGER NOT NULL CHECK (dpi >= 1),
            printable_width_dots INTEGER NOT NULL CHECK (printable_width_dots >= 1),
            render_mode TEXT NOT NULL CHECK (
                render_mode IN ('BitmapRaster', 'NativeTextExperimental')),
            thai_encoding_mode TEXT NOT NULL CHECK (
                thai_encoding_mode IN ('UnicodeRaster', 'NativeCodePageExperimental')),
            status_mode TEXT NOT NULL CHECK (status_mode IN ('Unsupported', 'SnmpPrinterMib')),
            supports_cash_drawer INTEGER NOT NULL CHECK (supports_cash_drawer IN (0, 1)),
            cutter_command_hex TEXT NULL CHECK (
                cutter_command_hex IS NULL OR (
                    length(cutter_command_hex) > 0
                    AND
                    length(cutter_command_hex) % 2 = 0
                    AND cutter_command_hex NOT GLOB '*[^0-9A-Fa-f]*')),
            drawer_pulse_command_hex TEXT NULL CHECK (
                drawer_pulse_command_hex IS NULL OR (
                    length(drawer_pulse_command_hex) > 0
                    AND
                    length(drawer_pulse_command_hex) % 2 = 0
                    AND drawer_pulse_command_hex NOT GLOB '*[^0-9A-Fa-f]*')),
            status_command_profile TEXT NULL CHECK (
                status_command_profile IS NULL OR length(trim(status_command_profile)) > 0),
            connect_timeout_ms INTEGER NOT NULL CHECK (connect_timeout_ms >= 1),
            send_timeout_ms INTEGER NOT NULL CHECK (send_timeout_ms >= 1),
            maximum_automatic_retries_before_send INTEGER NOT NULL CHECK (
                maximum_automatic_retries_before_send >= 0),
            automatic_retry_backoff_ms INTEGER NOT NULL CHECK (
                automatic_retry_backoff_ms >= 1),
            is_active INTEGER NOT NULL CHECK (is_active IN (0, 1)),
            updated_at TEXT NOT NULL,
            CHECK ((supports_cash_drawer = 1) = (drawer_pulse_command_hex IS NOT NULL)),
            CHECK (status_mode = 'Unsupported' OR status_command_profile IS NOT NULL)
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_printer_profiles_active_station
            ON printer_profiles (branch_id, production_station_id)
            WHERE is_active = 1;

        CREATE INDEX IF NOT EXISTS ix_printer_profiles_branch
            ON printer_profiles (branch_id, production_station_id, printer_id);

        CREATE TABLE IF NOT EXISTS printer_profile_audit (
            audit_id TEXT NOT NULL PRIMARY KEY,
            printer_id TEXT NOT NULL,
            branch_id TEXT NOT NULL,
            previous_configuration_json TEXT NULL,
            current_configuration_json TEXT NOT NULL,
            employee_id TEXT NOT NULL,
            device_id TEXT NOT NULL,
            occurred_at TEXT NOT NULL,
            FOREIGN KEY (printer_id) REFERENCES printer_profiles (printer_id)
        );

        CREATE INDEX IF NOT EXISTS ix_printer_profile_audit_printer
            ON printer_profile_audit (printer_id, occurred_at, audit_id);
        """;

    private const string SchemaVersion9 = """
        CREATE UNIQUE INDEX IF NOT EXISTS ux_print_jobs_active_printer_lease
            ON print_jobs (assigned_printer_id)
            WHERE lease_owner IS NOT NULL;

        CREATE INDEX IF NOT EXISTS ix_print_jobs_printer_dispatch
            ON print_jobs (assigned_printer_id, status, next_retry_at, created_at);

        CREATE TABLE IF NOT EXISTS print_attempts (
            print_attempt_id TEXT NOT NULL PRIMARY KEY CHECK (
                length(trim(print_attempt_id)) > 0),
            print_job_id TEXT NOT NULL,
            printer_id TEXT NOT NULL,
            attempt_number INTEGER NOT NULL CHECK (attempt_number >= 1),
            worker_id TEXT NOT NULL CHECK (length(trim(worker_id)) > 0),
            payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64),
            status TEXT NOT NULL CHECK (status IN (
                'Connecting', 'Dispatching', 'PrintDispatched', 'Failed', 'Unknown')),
            automatic_retry_allowed INTEGER NOT NULL DEFAULT 0 CHECK (
                automatic_retry_allowed IN (0, 1)),
            error_code TEXT NULL,
            started_at TEXT NOT NULL,
            first_byte_at TEXT NULL,
            finished_at TEXT NULL,
            FOREIGN KEY (print_job_id) REFERENCES print_jobs (print_job_id),
            FOREIGN KEY (printer_id) REFERENCES printer_profiles (printer_id),
            UNIQUE (print_job_id, attempt_number),
            CHECK (status NOT IN ('Dispatching', 'PrintDispatched') OR first_byte_at IS NOT NULL),
            CHECK ((status IN ('PrintDispatched', 'Failed', 'Unknown')) = (finished_at IS NOT NULL))
        );

        CREATE INDEX IF NOT EXISTS ix_print_attempts_job
            ON print_attempts (print_job_id, attempt_number);

        CREATE INDEX IF NOT EXISTS ix_print_attempts_active
            ON print_attempts (status, started_at);

        CREATE TRIGGER IF NOT EXISTS print_attempt_terminal_immutable
        BEFORE UPDATE ON print_attempts
        WHEN OLD.status IN ('PrintDispatched', 'Failed', 'Unknown')
        BEGIN
            SELECT RAISE(ABORT, 'Terminal Print Attempt is immutable');
        END;
        """;

    private const string SchemaVersion10 = """
        CREATE TABLE IF NOT EXISTS rendered_print_payloads (
            print_job_id TEXT NOT NULL PRIMARY KEY,
            snapshot_id TEXT NOT NULL,
            printer_id TEXT NOT NULL,
            document_id TEXT NOT NULL CHECK (length(trim(document_id)) > 0),
            payload_bytes BLOB NOT NULL CHECK (length(payload_bytes) > 0),
            payload_sha256 TEXT NOT NULL CHECK (
                length(payload_sha256) = 64
                AND payload_sha256 NOT GLOB '*[^0-9A-F]*'),
            created_at TEXT NOT NULL,
            FOREIGN KEY (print_job_id) REFERENCES print_jobs (print_job_id),
            FOREIGN KEY (printer_id) REFERENCES printer_profiles (printer_id)
        );

        CREATE INDEX IF NOT EXISTS ix_rendered_print_payloads_snapshot
            ON rendered_print_payloads (snapshot_id, printer_id);

        CREATE TRIGGER IF NOT EXISTS rendered_print_payload_immutable_update
        BEFORE UPDATE ON rendered_print_payloads
        BEGIN
            SELECT RAISE(ABORT, 'Rendered Print Payload is immutable');
        END;
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

        if (storedVersion < 6)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion6, cancellationToken);
        }

        if (storedVersion < 7)
        {
            await MigrateToVersion7Async(connection, transaction, cancellationToken);
        }

        if (storedVersion < 8)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion8, cancellationToken);
        }

        if (storedVersion < 9)
        {
            await MigrateToVersion9Async(connection, transaction, cancellationToken);
        }

        if (storedVersion < 10)
        {
            await ExecuteAsync(connection, transaction, SchemaVersion10, cancellationToken);
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

    private static async Task MigrateToVersion7Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(
                connection,
                transaction,
                "master_data_sellable_items",
                "production_station_id",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                ALTER TABLE master_data_sellable_items
                    ADD COLUMN production_station_id TEXT NULL
                    CHECK (production_station_id IS NULL OR length(trim(production_station_id)) > 0);
                """,
                cancellationToken);
        }

        if (!await ColumnExistsAsync(
                connection,
                transaction,
                "order_lines",
                "production_station_id",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                ALTER TABLE order_lines
                    ADD COLUMN production_station_id TEXT NULL
                    CHECK (production_station_id IS NULL OR length(trim(production_station_id)) > 0);
                """,
                cancellationToken);
        }

        await ExecuteAsync(
            connection,
            transaction,
            SchemaVersion7ProductionSnapshots,
            cancellationToken);

        var printJobsSql = await ReadTableSqlAsync(
            connection,
            transaction,
            "print_jobs",
            cancellationToken);
        if (!printJobsSql.Contains("KitchenOrder", StringComparison.Ordinal))
        {
            await ExecuteAsync(
                connection,
                transaction,
                SchemaVersion7PrintJobs,
                cancellationToken);
        }

        await ExecuteAsync(
            connection,
            transaction,
            SchemaVersion7PrintJobGuards,
            cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task MigrateToVersion9Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(
                connection,
                transaction,
                "print_jobs",
                "assigned_printer_id",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                ALTER TABLE print_jobs
                    ADD COLUMN assigned_printer_id TEXT NULL
                    REFERENCES printer_profiles (printer_id);
                """,
                cancellationToken);
        }

        if (!await ColumnExistsAsync(
                connection,
                transaction,
                "print_jobs",
                "lease_owner",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                ALTER TABLE print_jobs
                    ADD COLUMN lease_owner TEXT NULL CHECK (
                        lease_owner IS NULL OR length(trim(lease_owner)) > 0);
                """,
                cancellationToken);
        }

        if (!await ColumnExistsAsync(
                connection,
                transaction,
                "print_jobs",
                "lease_expires_at",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE print_jobs ADD COLUMN lease_expires_at TEXT NULL;",
                cancellationToken);
        }

        await ExecuteAsync(connection, transaction, SchemaVersion9, cancellationToken);
    }

    private static async Task<string> ReadTableSqlAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException($"Required table '{tableName}' was not found.");
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
