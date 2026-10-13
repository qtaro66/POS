using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Application.TableSessions;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed class SqliteTableSessionCommandTransaction : ITableSessionCommandTransaction
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly ISqliteBillPreviewContentSource _billPreviewContentSource;

    public SqliteTableSessionCommandTransaction(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ISqliteBillPreviewContentSource billPreviewContentSource)
    {
        _connection = connection;
        _transaction = transaction;
        _billPreviewContentSource = billPreviewContentSource;
    }

    public async Task<BranchCommandReceipt?> FindReceiptAsync(
        string branchId,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            SELECT request_id, branch_id, device_id, employee_id, client_created_at,
                   outcome, table_session_id, session_status, table_version,
                   error_code, server_received_at
            FROM command_receipts
            WHERE branch_id = $branchId AND request_id = $requestId;
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$requestId", requestId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BranchCommandReceipt(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ParseTimestamp(reader.GetString(4)),
            ParseEnum<BranchCommandOutcome>(reader.GetString(5)),
            TableSessionId.From(reader.GetString(6)),
            reader.IsDBNull(7) ? null : ParseEnum<TableSessionStatus>(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            ParseTimestamp(reader.GetString(10)));
    }

    public async Task<TableSession?> FindActiveSessionByTableAsync(
        string branchId,
        TableId tableId,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            SELECT table_session_id, table_id, status, version
            FROM table_sessions
            WHERE branch_id = $branchId
              AND table_id = $tableId
              AND status IN ('Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid');
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$tableId", tableId.Value);
        return await ReadSingleSessionAsync(command, cancellationToken);
    }

    public async Task<TableSession?> FindSessionAsync(
        string branchId,
        TableSessionId tableSessionId,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            SELECT table_session_id, table_id, status, version
            FROM table_sessions
            WHERE branch_id = $branchId AND table_session_id = $tableSessionId;
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$tableSessionId", tableSessionId.Value);
        return await ReadSingleSessionAsync(command, cancellationToken);
    }

    public async Task<bool> HasOpenCancellationRequestAsync(
        string branchId,
        TableSessionId tableSessionId,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM cancellation_requests
                WHERE branch_id = $branchId
                  AND table_session_id = $tableSessionId
                  AND status IN ('CancellationRequested', 'ProductionStatusChecking', 'StatusRecorded')
            );
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$tableSessionId", tableSessionId.Value);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    public Task<bool> HasActiveOrderLinesAsync(
        string branchId,
        TableSessionId tableSessionId,
        CancellationToken cancellationToken)
    {
        return _billPreviewContentSource.HasActiveOrderLinesAsync(
            _connection,
            _transaction,
            branchId,
            tableSessionId.Value,
            cancellationToken);
    }

    public Task<BillPreviewContent> CaptureBillPreviewContentAsync(
        BillPreviewCaptureRequest request,
        CancellationToken cancellationToken)
    {
        return _billPreviewContentSource.CaptureAsync(
            _connection,
            _transaction,
            request,
            cancellationToken);
    }

    public async Task InsertSessionAsync(
        string branchId,
        TableSession session,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            INSERT INTO table_sessions (
                branch_id, table_session_id, table_id, status, version)
            VALUES ($branchId, $tableSessionId, $tableId, $status, $version);
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        AddSessionParameters(command, session);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateSessionAsync(
        string branchId,
        TableSession session,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (session.Version != expectedVersion + 1)
        {
            throw new InvalidOperationException("The aggregate must advance exactly one version per update.");
        }

        using var command = CreateCommand("""
            UPDATE table_sessions
            SET table_id = $tableId,
                status = $status,
                version = $version
            WHERE branch_id = $branchId
              AND table_session_id = $tableSessionId
              AND version = $expectedVersion;
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$expectedVersion", expectedVersion);
        AddSessionParameters(command, session);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("The persisted table-session version changed during the transaction.");
        }
    }

    public async Task AddReceiptAsync(
        BranchCommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            INSERT INTO command_receipts (
                branch_id, request_id, device_id, employee_id, client_created_at,
                outcome, table_session_id, session_status, table_version,
                error_code, server_received_at)
            VALUES (
                $branchId, $requestId, $deviceId, $employeeId, $clientCreatedAt,
                $outcome, $tableSessionId, $sessionStatus, $tableVersion,
                $errorCode, $serverReceivedAt);
            """);
        command.Parameters.AddWithValue("$branchId", receipt.BranchId);
        command.Parameters.AddWithValue("$requestId", receipt.RequestId);
        command.Parameters.AddWithValue("$deviceId", receipt.DeviceId);
        command.Parameters.AddWithValue("$employeeId", receipt.EmployeeId);
        command.Parameters.AddWithValue("$clientCreatedAt", FormatTimestamp(receipt.ClientCreatedAt));
        command.Parameters.AddWithValue("$outcome", receipt.Outcome.ToString());
        command.Parameters.AddWithValue("$tableSessionId", receipt.TableSessionId.Value);
        command.Parameters.AddWithValue("$sessionStatus", DbValue(receipt.SessionStatus?.ToString()));
        command.Parameters.AddWithValue("$tableVersion", DbValue(receipt.TableVersion));
        command.Parameters.AddWithValue("$errorCode", DbValue(receipt.ErrorCode));
        command.Parameters.AddWithValue("$serverReceivedAt", FormatTimestamp(receipt.ServerReceivedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAuditAsync(
        TableSessionAuditRecord auditRecord,
        CancellationToken cancellationToken)
    {
        var transition = auditRecord.Transition;
        using var command = CreateCommand("""
            INSERT INTO table_session_audit (
                audit_id, branch_id, request_id, table_session_id, action,
                previous_status, current_status, previous_table_id, current_table_id,
                table_version, employee_id, device_id, occurred_at, reason)
            VALUES (
                $auditId, $branchId, $requestId, $tableSessionId, $action,
                $previousStatus, $currentStatus, $previousTableId, $currentTableId,
                $tableVersion, $employeeId, $deviceId, $occurredAt, $reason);
            """);
        command.Parameters.AddWithValue("$auditId", auditRecord.AuditId);
        command.Parameters.AddWithValue("$branchId", auditRecord.BranchId);
        command.Parameters.AddWithValue("$requestId", auditRecord.RequestId);
        command.Parameters.AddWithValue("$tableSessionId", transition.TableSessionId.Value);
        command.Parameters.AddWithValue("$action", transition.Action.ToString());
        command.Parameters.AddWithValue("$previousStatus", DbValue(transition.PreviousStatus?.ToString()));
        command.Parameters.AddWithValue("$currentStatus", transition.CurrentStatus.ToString());
        command.Parameters.AddWithValue("$previousTableId", transition.PreviousTableId.Value);
        command.Parameters.AddWithValue("$currentTableId", transition.CurrentTableId.Value);
        command.Parameters.AddWithValue("$tableVersion", transition.Version);
        command.Parameters.AddWithValue("$employeeId", transition.EmployeeId);
        command.Parameters.AddWithValue("$deviceId", transition.DeviceId);
        command.Parameters.AddWithValue("$occurredAt", FormatTimestamp(transition.OccurredAt));
        command.Parameters.AddWithValue("$reason", DbValue(transition.Reason));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddCloudOutboxAsync(
        CloudSyncOutboxRecord outboxRecord,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            INSERT INTO cloud_sync_outbox (
                outbox_id, branch_id, request_id, table_session_id,
                action, table_version, created_at)
            VALUES (
                $outboxId, $branchId, $requestId, $tableSessionId,
                $action, $tableVersion, $createdAt);
            """);
        command.Parameters.AddWithValue("$outboxId", outboxRecord.OutboxId);
        command.Parameters.AddWithValue("$branchId", outboxRecord.BranchId);
        command.Parameters.AddWithValue("$requestId", outboxRecord.RequestId);
        command.Parameters.AddWithValue("$tableSessionId", outboxRecord.TableSessionId.Value);
        command.Parameters.AddWithValue("$action", outboxRecord.Action.ToString());
        command.Parameters.AddWithValue("$tableVersion", outboxRecord.TableVersion);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(outboxRecord.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddBillPreviewSnapshotAsync(
        BillPreviewSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            INSERT INTO bill_preview_snapshots (
                snapshot_id, branch_id, request_id, table_session_id, table_id,
                document_label, content_schema_version, content_json, content_sha256, created_at)
            VALUES (
                $snapshotId, $branchId, $requestId, $tableSessionId, $tableId,
                $documentLabel, $contentSchemaVersion, $contentJson, $contentSha256, $createdAt);
            """);
        command.Parameters.AddWithValue("$snapshotId", snapshot.SnapshotId);
        command.Parameters.AddWithValue("$branchId", snapshot.BranchId);
        command.Parameters.AddWithValue("$requestId", snapshot.RequestId);
        command.Parameters.AddWithValue("$tableSessionId", snapshot.TableSessionId.Value);
        command.Parameters.AddWithValue("$tableId", snapshot.TableId.Value);
        command.Parameters.AddWithValue("$documentLabel", snapshot.DocumentLabel);
        command.Parameters.AddWithValue("$contentSchemaVersion", snapshot.Content.SchemaVersion);
        command.Parameters.AddWithValue("$contentJson", snapshot.Content.Json);
        command.Parameters.AddWithValue("$contentSha256", snapshot.Content.Sha256);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(snapshot.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddPrintJobAsync(
        PersistentPrintJob printJob,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand("""
            INSERT INTO print_jobs (
                print_job_id, branch_id, request_id, table_session_id, snapshot_id,
                purpose, target_station, status, attempt_count, created_at,
                original_print_job_id, is_reprint)
            VALUES (
                $printJobId, $branchId, $requestId, $tableSessionId, $snapshotId,
                $purpose, $targetStation, $status, $attemptCount, $createdAt,
                $originalPrintJobId, $isReprint);
            """);
        command.Parameters.AddWithValue("$printJobId", printJob.PrintJobId);
        command.Parameters.AddWithValue("$branchId", printJob.BranchId);
        command.Parameters.AddWithValue("$requestId", printJob.RequestId);
        command.Parameters.AddWithValue("$tableSessionId", printJob.TableSessionId.Value);
        command.Parameters.AddWithValue("$snapshotId", printJob.SnapshotId);
        command.Parameters.AddWithValue("$purpose", printJob.Purpose.ToString());
        command.Parameters.AddWithValue("$targetStation", printJob.TargetStation.ToString());
        command.Parameters.AddWithValue("$status", printJob.Status.ToString());
        command.Parameters.AddWithValue("$attemptCount", printJob.AttemptCount);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(printJob.CreatedAt));
        command.Parameters.AddWithValue("$originalPrintJobId", DbValue(printJob.OriginalPrintJobId));
        command.Parameters.AddWithValue("$isReprint", printJob.IsReprint ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteCommand CreateCommand(string commandText)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = commandText;
        return command;
    }

    private static async Task<TableSession?> ReadSingleSessionAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var session = TableSession.Rehydrate(
            TableSessionId.From(reader.GetString(0)),
            TableId.From(reader.GetString(1)),
            ParseEnum<TableSessionStatus>(reader.GetString(2)),
            reader.GetInt64(3));

        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The branch database contains duplicate active table sessions.");
        }

        return session;
    }

    private static void AddSessionParameters(SqliteCommand command, TableSession session)
    {
        command.Parameters.AddWithValue("$tableSessionId", session.Id.Value);
        command.Parameters.AddWithValue("$tableId", session.CurrentTableId.Value);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$version", session.Version);
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new InvalidOperationException($"The database value '{value}' is not a valid {typeof(TEnum).Name}.");
        }

        return parsed;
    }
}
