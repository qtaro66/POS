using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

internal static class SqliteBranchSchema
{
    public const int CurrentVersion = 1;

    public static async Task InitializeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var settingsCommand = connection.CreateCommand();
        settingsCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;";
        await settingsCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction(deferred: false);
        using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "PRAGMA user_version;";
        var storedVersion = Convert.ToInt32(
            await versionCommand.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (storedVersion == CurrentVersion)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (storedVersion != 0)
        {
            throw new InvalidOperationException(
                $"Unsupported branch database schema version {storedVersion}; expected {CurrentVersion}.");
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
