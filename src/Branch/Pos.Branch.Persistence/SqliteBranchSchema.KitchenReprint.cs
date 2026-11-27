using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

internal static partial class SqliteBranchSchema
{
    private static async Task MigrateKitchenReprintAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE kitchen_reprint_snapshots (
                snapshot_id TEXT NOT NULL PRIMARY KEY,
                branch_id TEXT NOT NULL, request_id TEXT NOT NULL, table_session_id TEXT NOT NULL,
                order_batch_id TEXT NOT NULL, order_line_id TEXT NOT NULL, production_station_id TEXT NOT NULL,
                original_snapshot_id TEXT NOT NULL REFERENCES production_order_snapshots(snapshot_id),
                original_print_job_id TEXT NOT NULL REFERENCES print_jobs(print_job_id),
                content_schema_version INTEGER NOT NULL CHECK(content_schema_version=1),
                content_json TEXT NOT NULL CHECK(json_valid(content_json)),
                content_sha256 TEXT NOT NULL CHECK(length(content_sha256)=64),
                created_at TEXT NOT NULL, UNIQUE(branch_id,request_id),
                FOREIGN KEY(branch_id,table_session_id) REFERENCES table_sessions(branch_id,table_session_id),
                FOREIGN KEY(branch_id,order_batch_id) REFERENCES order_batches(branch_id,order_batch_id)
            );
            CREATE TABLE kitchen_reprint_requests (
                branch_id TEXT NOT NULL, request_id TEXT NOT NULL, device_id TEXT NOT NULL,
                requester_employee_id TEXT NOT NULL, approver_employee_id TEXT NOT NULL,
                cash_request_id TEXT NOT NULL, original_snapshot_id TEXT NOT NULL REFERENCES production_order_snapshots(snapshot_id),
                order_line_id TEXT NOT NULL, original_print_job_id TEXT NOT NULL REFERENCES print_jobs(print_job_id),
                reason TEXT NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 4096),
                command_json TEXT NOT NULL CHECK(json_valid(command_json)), receipt_json TEXT NOT NULL CHECK(json_valid(receipt_json)),
                created_at TEXT NOT NULL, PRIMARY KEY(branch_id,request_id),
                FOREIGN KEY(branch_id,cash_request_id) REFERENCES cash_sales(branch_id,request_id),
                FOREIGN KEY(branch_id,request_id) REFERENCES kitchen_reprint_snapshots(branch_id,request_id)
            );
            CREATE TABLE kitchen_reprint_audit (
                branch_id TEXT NOT NULL, request_id TEXT NOT NULL,
                reprint_print_job_id TEXT NOT NULL UNIQUE REFERENCES print_jobs(print_job_id),
                device_id TEXT NOT NULL, requester_employee_id TEXT NOT NULL, approver_employee_id TEXT NOT NULL,
                reason TEXT NOT NULL, outcome TEXT NOT NULL CHECK(outcome='Queued'), created_at TEXT NOT NULL,
                PRIMARY KEY(branch_id,request_id),
                FOREIGN KEY(branch_id,request_id) REFERENCES kitchen_reprint_requests(branch_id,request_id)
            );
            CREATE TRIGGER kitchen_reprint_snapshots_no_update BEFORE UPDATE ON kitchen_reprint_snapshots BEGIN SELECT RAISE(ABORT,'Immutable reprint snapshot'); END;
            CREATE TRIGGER kitchen_reprint_snapshots_no_delete BEFORE DELETE ON kitchen_reprint_snapshots BEGIN SELECT RAISE(ABORT,'Immutable reprint snapshot'); END;
            CREATE TRIGGER kitchen_reprint_requests_no_update BEFORE UPDATE ON kitchen_reprint_requests BEGIN SELECT RAISE(ABORT,'Immutable reprint request'); END;
            CREATE TRIGGER kitchen_reprint_requests_no_delete BEFORE DELETE ON kitchen_reprint_requests BEGIN SELECT RAISE(ABORT,'Immutable reprint request'); END;
            CREATE TRIGGER kitchen_reprint_audit_no_update BEFORE UPDATE ON kitchen_reprint_audit BEGIN SELECT RAISE(ABORT,'Immutable reprint audit'); END;
            CREATE TRIGGER kitchen_reprint_audit_no_delete BEFORE DELETE ON kitchen_reprint_audit BEGIN SELECT RAISE(ABORT,'Immutable reprint audit'); END;
            DROP TRIGGER IF EXISTS print_jobs_snapshot_insert_guard;
            DROP TRIGGER IF EXISTS print_jobs_snapshot_update_guard;
            """, token);
        await ExecuteAsync(connection, transaction, SchemaVersion7PrintJobGuards, token);
        const string condition = """
            NEW.purpose='KitchenOrder' AND (
                (NEW.is_reprint=0 AND NEW.original_print_job_id IS NOT NULL) OR
                (NEW.is_reprint=1 AND NOT EXISTS (
                    SELECT 1 FROM kitchen_reprint_snapshots copy
                    JOIN production_order_snapshots source ON source.snapshot_id=copy.original_snapshot_id
                    JOIN print_jobs original ON original.print_job_id=copy.original_print_job_id
                    WHERE copy.snapshot_id=NEW.snapshot_id AND copy.branch_id=NEW.branch_id AND copy.request_id=NEW.request_id
                        AND copy.table_session_id=NEW.table_session_id AND copy.production_station_id=NEW.target_station
                        AND copy.original_print_job_id=NEW.original_print_job_id
                        AND source.branch_id=NEW.branch_id AND source.table_session_id=NEW.table_session_id
                        AND source.production_station_id=NEW.target_station AND source.order_batch_id=copy.order_batch_id
                        AND original.branch_id=NEW.branch_id AND original.table_session_id=NEW.table_session_id
                        AND original.snapshot_id=source.snapshot_id AND original.request_id=source.request_id
                        AND original.target_station=NEW.target_station AND original.purpose='KitchenOrder'
                        AND original.is_reprint=0 AND original.original_print_job_id IS NULL))
            )
            """;
        await ExecuteAsync(connection, transaction, $"""
            CREATE TRIGGER print_jobs_kitchen_reprint_insert_guard BEFORE INSERT ON print_jobs WHEN {condition}
            BEGIN SELECT RAISE(ABORT,'Invalid kitchen reprint references'); END;
            CREATE TRIGGER print_jobs_kitchen_reprint_update_guard BEFORE UPDATE OF snapshot_id,purpose,target_station,
                branch_id,request_id,table_session_id,is_reprint,original_print_job_id ON print_jobs WHEN {condition}
            BEGIN SELECT RAISE(ABORT,'Invalid kitchen reprint references'); END;
            """, token);
    }
}
