using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Printing.Abstractions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record PrintJobLease(
    string PrintJobId,
    string BranchId,
    string ProductionStationId,
    string SnapshotId,
    string PrinterId,
    string WorkerId,
    DateTimeOffset LeaseExpiresAt,
    int AttemptCount,
    int MaximumAutomaticRetriesBeforeSend,
    TimeSpan AutomaticRetryBackoff);

public sealed record PrintLeaseRecoveryResult(
    int ReleasedBeforeAttempt,
    int FailedBeforeFirstByte,
    int MarkedUnknownAfterFirstByte);

public sealed class SqlitePrintDispatchStore
{
    private readonly SqliteConnectionFactory _connections;

    public SqlitePrintDispatchStore(string databasePath)
    {
        _connections = new SqliteConnectionFactory(databasePath);
    }

    public async Task<PrintJobLease?> ClaimNextAsync(
        string printerId,
        string workerId,
        DateTimeOffset claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        printerId = Require(printerId, nameof(printerId));
        workerId = Require(workerId, nameof(workerId));
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            await RecoverExpiredLeasesCoreAsync(
                connection,
                transaction,
                claimedAt,
                printerId,
                cancellationToken);
            await PromoteDueRetryAsync(
                connection,
                transaction,
                printerId,
                claimedAt,
                cancellationToken);

            var candidate = await FindCandidateAsync(
                connection,
                transaction,
                printerId,
                cancellationToken);
            if (candidate is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var leaseExpiresAt = claimedAt.Add(leaseDuration);
            using var claim = connection.CreateCommand();
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE print_jobs
                SET assigned_printer_id = COALESCE(assigned_printer_id, $printerId),
                    lease_owner = $workerId,
                    lease_expires_at = $leaseExpiresAt
                WHERE print_job_id = $printJobId
                  AND status = 'PrintQueued'
                  AND lease_owner IS NULL
                  AND (assigned_printer_id IS NULL OR assigned_printer_id = $printerId);
                """;
            claim.Parameters.AddWithValue("$printerId", printerId);
            claim.Parameters.AddWithValue("$workerId", workerId);
            claim.Parameters.AddWithValue("$leaseExpiresAt", Timestamp(leaseExpiresAt));
            claim.Parameters.AddWithValue("$printJobId", candidate.PrintJobId);
            if (await claim.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The selected print job could not be leased.");
            }

            await transaction.CommitAsync(cancellationToken);
            return candidate with
            {
                WorkerId = workerId,
                LeaseExpiresAt = leaseExpiresAt
            };
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task<PrintJobLease> RenewLeaseAsync(
        PrintJobLease lease,
        DateTimeOffset renewedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var leaseExpiresAt = renewedAt.Add(leaseDuration);
        if (leaseExpiresAt <= lease.LeaseExpiresAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "The renewed lease must extend the current lease expiry.");
        }

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            using var renew = connection.CreateCommand();
            renew.Transaction = transaction;
            renew.CommandText = """
                UPDATE print_jobs
                SET lease_expires_at = $leaseExpiresAt
                WHERE print_job_id = $printJobId
                  AND assigned_printer_id = $printerId
                  AND lease_owner = $workerId
                  AND lease_expires_at = $currentLeaseExpiresAt
                  AND lease_expires_at > $renewedAt
                  AND status IN ('PrintQueued', 'Dispatching');
                """;
            AddLeaseParameters(renew, lease);
            renew.Parameters.AddWithValue(
                "$currentLeaseExpiresAt",
                Timestamp(lease.LeaseExpiresAt));
            renew.Parameters.AddWithValue("$renewedAt", Timestamp(renewedAt));
            renew.Parameters.AddWithValue("$leaseExpiresAt", Timestamp(leaseExpiresAt));
            if (await renew.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The print-job lease is no longer active or is not owned by this lease token.");
            }

            await transaction.CommitAsync(cancellationToken);
            return lease with { LeaseExpiresAt = leaseExpiresAt };
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task<int> StartAttemptAsync(
        PrintJobLease lease,
        string printAttemptId,
        string payloadSha256,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        printAttemptId = Require(printAttemptId, nameof(printAttemptId));
        payloadSha256 = RequireSha256(payloadSha256);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            var existingAttempt = await FindExistingAttemptAsync(
                connection,
                transaction,
                printAttemptId,
                cancellationToken);
            if (existingAttempt is not null)
            {
                if (!string.Equals(existingAttempt.Value.PrintJobId, lease.PrintJobId, StringComparison.Ordinal) ||
                    !string.Equals(existingAttempt.Value.PrinterId, lease.PrinterId, StringComparison.Ordinal) ||
                    !string.Equals(existingAttempt.Value.WorkerId, lease.WorkerId, StringComparison.Ordinal) ||
                    !string.Equals(existingAttempt.Value.PayloadSha256, payloadSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The print-attempt identifier is already used by different data.");
                }

                await transaction.CommitAsync(cancellationToken);
                return existingAttempt.Value.AttemptNumber;
            }

            var currentAttemptCount = await ReadLeasedAttemptCountAsync(
                connection,
                transaction,
                lease,
                startedAt,
                cancellationToken);
            var attemptNumber = checked(currentAttemptCount + 1);

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO print_attempts (
                    print_attempt_id, print_job_id, printer_id, attempt_number,
                    worker_id, payload_sha256, status, started_at)
                VALUES (
                    $printAttemptId, $printJobId, $printerId, $attemptNumber,
                    $workerId, $payloadSha256, 'Connecting', $startedAt);

                UPDATE print_jobs
                SET attempt_count = $attemptNumber,
                    last_error = NULL,
                    next_retry_at = NULL,
                    blocked_reason = NULL
                WHERE print_job_id = $printJobId
                  AND assigned_printer_id = $printerId
                  AND lease_owner = $workerId
                  AND lease_expires_at > $startedAt
                  AND status = 'PrintQueued';
                """;
            insert.Parameters.AddWithValue("$printAttemptId", printAttemptId);
            insert.Parameters.AddWithValue("$printJobId", lease.PrintJobId);
            insert.Parameters.AddWithValue("$printerId", lease.PrinterId);
            insert.Parameters.AddWithValue("$attemptNumber", attemptNumber);
            insert.Parameters.AddWithValue("$workerId", lease.WorkerId);
            insert.Parameters.AddWithValue("$payloadSha256", payloadSha256);
            insert.Parameters.AddWithValue("$startedAt", Timestamp(startedAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);

            if (await ReadAttemptCountAsync(
                    connection,
                    transaction,
                    lease.PrintJobId,
                    cancellationToken) != attemptNumber)
            {
                throw new InvalidOperationException("The print job attempt count was not advanced.");
            }

            await transaction.CommitAsync(cancellationToken);
            return attemptNumber;
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task MarkFirstByteSendingAsync(
        PrintJobLease lease,
        string printAttemptId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        printAttemptId = Require(printAttemptId, nameof(printAttemptId));

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            var currentState = await ReadAttemptStateAsync(
                connection,
                transaction,
                lease,
                printAttemptId,
                cancellationToken);
            if (currentState.Status == "Dispatching")
            {
                if (!await JobHasStatusAsync(
                        connection,
                        transaction,
                        lease.PrintJobId,
                        "Dispatching",
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        "The persisted print attempt and print job disagree.");
                }

                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (currentState.Status != "Connecting")
            {
                throw new InvalidOperationException("The print attempt is not ready to dispatch.");
            }

            using var attempt = connection.CreateCommand();
            attempt.Transaction = transaction;
            attempt.CommandText = """
                UPDATE print_attempts
                SET status = 'Dispatching', first_byte_at = $occurredAt
                WHERE print_attempt_id = $printAttemptId
                  AND print_job_id = $printJobId
                  AND printer_id = $printerId
                  AND worker_id = $workerId
                  AND status = 'Connecting';
                """;
            AddLeaseParameters(attempt, lease);
            attempt.Parameters.AddWithValue("$printAttemptId", printAttemptId);
            attempt.Parameters.AddWithValue("$occurredAt", Timestamp(occurredAt));
            if (await attempt.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The print attempt is not ready to dispatch.");
            }

            using var job = connection.CreateCommand();
            job.Transaction = transaction;
            job.CommandText = """
                UPDATE print_jobs
                SET status = 'Dispatching'
                WHERE print_job_id = $printJobId
                  AND assigned_printer_id = $printerId
                  AND lease_owner = $workerId
                  AND lease_expires_at > $occurredAt
                  AND status = 'PrintQueued';
                """;
            AddLeaseParameters(job, lease);
            job.Parameters.AddWithValue("$occurredAt", Timestamp(occurredAt));
            if (await job.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The print job is not ready to dispatch.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task CompleteAttemptAsync(
        PrintJobLease lease,
        string printAttemptId,
        PrintDispatchResult result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(result);
        printAttemptId = Require(printAttemptId, nameof(printAttemptId));
        if (!string.Equals(result.PrintJobId, lease.PrintJobId, StringComparison.Ordinal) ||
            !string.Equals(result.PrinterId, lease.PrinterId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The dispatch result does not match the lease.", nameof(result));
        }

        if (result.Outcome is PrintDispatchOutcome.Blocked)
        {
            throw new ArgumentException("A blocked dispatch has no transport attempt to complete.", nameof(result));
        }

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            var state = await ReadAttemptStateAsync(
                connection,
                transaction,
                lease,
                printAttemptId,
                cancellationToken);
            var terminalStatus = result.Outcome.ToString();
            if (state.Status == terminalStatus)
            {
                if (!await JobHasStatusAsync(
                        connection,
                        transaction,
                        lease.PrintJobId,
                        terminalStatus,
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        "The persisted print attempt and print job disagree.");
                }

                await transaction.CommitAsync(cancellationToken);
                return;
            }

            ValidateCompletion(state.Status, result);
            var retryAllowed = result.Outcome is PrintDispatchOutcome.Failed &&
                result.AutomaticRetryAllowed &&
                state.AttemptNumber <= lease.MaximumAutomaticRetriesBeforeSend;
            var nextRetryAt = retryAllowed
                ? completedAt.Add(lease.AutomaticRetryBackoff)
                : (DateTimeOffset?)null;
            using var attempt = connection.CreateCommand();
            attempt.Transaction = transaction;
            attempt.CommandText = """
                UPDATE print_attempts
                SET status = $status,
                    automatic_retry_allowed = $retryAllowed,
                    error_code = $errorCode,
                    finished_at = $finishedAt
                WHERE print_attempt_id = $printAttemptId
                  AND print_job_id = $printJobId
                  AND printer_id = $printerId
                  AND worker_id = $workerId
                  AND status = $expectedStatus;
                """;
            AddLeaseParameters(attempt, lease);
            attempt.Parameters.AddWithValue("$printAttemptId", printAttemptId);
            attempt.Parameters.AddWithValue("$status", terminalStatus);
            attempt.Parameters.AddWithValue("$retryAllowed", retryAllowed ? 1 : 0);
            attempt.Parameters.AddWithValue("$errorCode", (object?)result.ErrorCode ?? DBNull.Value);
            attempt.Parameters.AddWithValue("$finishedAt", Timestamp(completedAt));
            attempt.Parameters.AddWithValue("$expectedStatus", state.Status);
            if (await attempt.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The print attempt completion was not persisted.");
            }

            using var job = connection.CreateCommand();
            job.Transaction = transaction;
            job.CommandText = """
                UPDATE print_jobs
                SET status = $status,
                    next_retry_at = $nextRetryAt,
                    last_error = $errorCode,
                    blocked_reason = NULL,
                    lease_owner = NULL,
                    lease_expires_at = NULL
                WHERE print_job_id = $printJobId
                  AND assigned_printer_id = $printerId
                  AND lease_owner = $workerId
                  AND lease_expires_at > $completedAt
                  AND status = $expectedJobStatus;
                """;
            AddLeaseParameters(job, lease);
            job.Parameters.AddWithValue("$status", terminalStatus);
            job.Parameters.AddWithValue(
                "$nextRetryAt",
                nextRetryAt is null ? DBNull.Value : Timestamp(nextRetryAt.Value));
            job.Parameters.AddWithValue("$errorCode", (object?)result.ErrorCode ?? DBNull.Value);
            job.Parameters.AddWithValue("$completedAt", Timestamp(completedAt));
            job.Parameters.AddWithValue(
                "$expectedJobStatus",
                state.Status == "Dispatching" ? "Dispatching" : "PrintQueued");
            if (await job.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The print job completion was not persisted.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    public async Task ReleaseBlockedAsync(
        PrintJobLease lease,
        string blockedReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        blockedReason = Require(blockedReason, nameof(blockedReason));
        await using var connection = await _connections.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE print_jobs
            SET blocked_reason = $blockedReason,
                lease_owner = NULL,
                lease_expires_at = NULL
            WHERE print_job_id = $printJobId
              AND assigned_printer_id = $printerId
              AND lease_owner = $workerId
              AND status = 'PrintQueued'
              AND NOT EXISTS (
                  SELECT 1 FROM print_attempts
                  WHERE print_job_id = $printJobId
                    AND status IN ('Connecting', 'Dispatching'));
            """;
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue("$blockedReason", blockedReason);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The blocked print-job lease was not released.");
        }
    }

    public async Task<PrintLeaseRecoveryResult> RecoverExpiredLeasesAsync(
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            var result = await RecoverExpiredLeasesCoreAsync(
                connection,
                transaction,
                recoveredAt,
                printerId: null,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private static async Task<PrintLeaseRecoveryResult> RecoverExpiredLeasesCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset recoveredAt,
        string? printerId,
        CancellationToken cancellationToken)
    {
        var printerPredicate = printerId is null
            ? string.Empty
            : " AND j.assigned_printer_id = $printerId";

        var unknownAttempts = await ExecuteRecoveryUpdateAsync(
            connection,
            transaction,
            $$"""
            UPDATE print_attempts AS a
            SET status = 'Unknown',
                automatic_retry_allowed = 0,
                error_code = 'WorkerLeaseExpiredAfterFirstByte',
                finished_at = $recoveredAt
            WHERE a.status = 'Dispatching'
              AND EXISTS (
                  SELECT 1 FROM print_jobs AS j
                  WHERE j.print_job_id = a.print_job_id
                    AND j.lease_owner IS NOT NULL
                    AND j.lease_expires_at <= $recoveredAt{{printerPredicate}});
            """,
            printerId,
            recoveredAt,
            cancellationToken);

        await ExecuteRecoveryUpdateAsync(
            connection,
            transaction,
            $$"""
            UPDATE print_jobs AS j
            SET status = 'Unknown',
                next_retry_at = NULL,
                last_error = 'WorkerLeaseExpiredAfterFirstByte',
                lease_owner = NULL,
                lease_expires_at = NULL
            WHERE j.status = 'Dispatching'
              AND j.lease_owner IS NOT NULL
              AND j.lease_expires_at <= $recoveredAt{{printerPredicate}};
            """,
            printerId,
            recoveredAt,
            cancellationToken);

        var failedAttempts = await ExecuteRecoveryUpdateAsync(
            connection,
            transaction,
            $$"""
            UPDATE print_attempts AS a
            SET status = 'Failed',
                automatic_retry_allowed = CASE WHEN a.attempt_number <= (
                    SELECT p.maximum_automatic_retries_before_send
                    FROM printer_profiles AS p
                    WHERE p.printer_id = a.printer_id) THEN 1 ELSE 0 END,
                error_code = 'WorkerLeaseExpiredBeforeFirstByte',
                finished_at = $recoveredAt
            WHERE a.status = 'Connecting'
              AND EXISTS (
                  SELECT 1 FROM print_jobs AS j
                  WHERE j.print_job_id = a.print_job_id
                    AND j.lease_owner IS NOT NULL
                    AND j.lease_expires_at <= $recoveredAt{{printerPredicate}});
            """,
            printerId,
            recoveredAt,
            cancellationToken);

        await ExecuteRecoveryUpdateAsync(
            connection,
            transaction,
            $$"""
            UPDATE print_jobs AS j
            SET status = 'Failed',
                next_retry_at = CASE WHEN j.attempt_count <= (
                    SELECT p.maximum_automatic_retries_before_send
                    FROM printer_profiles AS p
                    WHERE p.printer_id = j.assigned_printer_id)
                    THEN $recoveredAt ELSE NULL END,
                last_error = 'WorkerLeaseExpiredBeforeFirstByte',
                lease_owner = NULL,
                lease_expires_at = NULL
            WHERE j.status = 'PrintQueued'
              AND j.lease_owner IS NOT NULL
              AND j.lease_expires_at <= $recoveredAt
              AND EXISTS (
                  SELECT 1 FROM print_attempts AS a
                  WHERE a.print_job_id = j.print_job_id
                    AND a.status = 'Failed'
                    AND a.error_code = 'WorkerLeaseExpiredBeforeFirstByte'
                    AND a.finished_at = $recoveredAt){{printerPredicate}};
            """,
            printerId,
            recoveredAt,
            cancellationToken);

        var released = await ExecuteRecoveryUpdateAsync(
            connection,
            transaction,
            $$"""
            UPDATE print_jobs AS j
            SET lease_owner = NULL, lease_expires_at = NULL
            WHERE j.status = 'PrintQueued'
              AND j.lease_owner IS NOT NULL
              AND j.lease_expires_at <= $recoveredAt
              AND NOT EXISTS (
                  SELECT 1 FROM print_attempts AS a
                  WHERE a.print_job_id = j.print_job_id
                    AND a.status IN ('Connecting', 'Dispatching')){{printerPredicate}};
            """,
            printerId,
            recoveredAt,
            cancellationToken);

        return new PrintLeaseRecoveryResult(released, failedAttempts, unknownAttempts);
    }

    private static async Task PromoteDueRetryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string printerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE print_jobs
            SET status = 'PrintQueued', next_retry_at = NULL
            WHERE assigned_printer_id = $printerId
              AND status = 'Failed'
              AND lease_owner IS NULL
              AND next_retry_at IS NOT NULL
              AND next_retry_at <= $now;
            """;
        command.Parameters.AddWithValue("$printerId", printerId);
        command.Parameters.AddWithValue("$now", Timestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PrintJobLease?> FindCandidateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string printerId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT j.print_job_id, j.branch_id, j.target_station, j.snapshot_id,
                   j.attempt_count, p.maximum_automatic_retries_before_send,
                   p.automatic_retry_backoff_ms
            FROM printer_profiles AS p
            JOIN print_jobs AS j
              ON j.branch_id = p.branch_id
             AND j.target_station = p.production_station_id
            WHERE p.printer_id = $printerId
              AND p.is_active = 1
              AND j.status = 'PrintQueued'
              AND j.lease_owner IS NULL
              AND (j.assigned_printer_id IS NULL OR j.assigned_printer_id = p.printer_id)
              AND NOT EXISTS (
                  SELECT 1 FROM print_jobs AS active
                  WHERE active.assigned_printer_id = p.printer_id
                    AND active.lease_owner IS NOT NULL)
            ORDER BY j.created_at, j.print_job_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$printerId", printerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PrintJobLease(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            printerId,
            string.Empty,
            DateTimeOffset.MinValue,
            reader.GetInt32(4),
            reader.GetInt32(5),
            TimeSpan.FromMilliseconds(reader.GetInt32(6)));
    }

    private static async Task<int> ReadLeasedAttemptCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PrintJobLease lease,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attempt_count
            FROM print_jobs
            WHERE print_job_id = $printJobId
              AND assigned_printer_id = $printerId
              AND lease_owner = $workerId
              AND lease_expires_at > $startedAt
              AND status = 'PrintQueued'
              AND NOT EXISTS (
                  SELECT 1 FROM print_attempts
                  WHERE print_job_id = $printJobId
                    AND status IN ('Connecting', 'Dispatching'));
            """;
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue("$startedAt", Timestamp(startedAt));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is long count
            ? checked((int)count)
            : throw new InvalidOperationException("The print-job lease is not active.");
    }

    private static async Task<int> ReadAttemptCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string printJobId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT attempt_count FROM print_jobs WHERE print_job_id = $id;";
        command.Parameters.AddWithValue("$id", printJobId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<(string Status, int AttemptNumber)> ReadAttemptStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PrintJobLease lease,
        string printAttemptId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT status, attempt_number
            FROM print_attempts
            WHERE print_attempt_id = $printAttemptId
              AND print_job_id = $printJobId
              AND printer_id = $printerId
              AND worker_id = $workerId;
            """;
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue("$printAttemptId", printAttemptId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The active print attempt was not found.");
        }

        return (reader.GetString(0), reader.GetInt32(1));
    }

    private static async Task<(
        string PrintJobId,
        string PrinterId,
        string WorkerId,
        string PayloadSha256,
        int AttemptNumber)?> FindExistingAttemptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string printAttemptId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT print_job_id, printer_id, worker_id, payload_sha256, attempt_number
            FROM print_attempts
            WHERE print_attempt_id = $printAttemptId;
            """;
        command.Parameters.AddWithValue("$printAttemptId", printAttemptId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4));
    }

    private static async Task<bool> JobHasStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string printJobId,
        string status,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM print_jobs
            WHERE print_job_id = $printJobId AND status = $status;
            """;
        command.Parameters.AddWithValue("$printJobId", printJobId);
        command.Parameters.AddWithValue("$status", status);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<int> ExecuteRecoveryUpdateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        string? printerId,
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$recoveredAt", Timestamp(recoveredAt));
        if (printerId is not null)
        {
            command.Parameters.AddWithValue("$printerId", printerId);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateCompletion(string attemptStatus, PrintDispatchResult result)
    {
        if (result.Outcome is PrintDispatchOutcome.PrintDispatched &&
            attemptStatus != "Dispatching")
        {
            throw new InvalidOperationException(
                "PrintDispatched requires a persisted first-byte boundary.");
        }

        if (result.Outcome is PrintDispatchOutcome.Failed && attemptStatus != "Connecting")
        {
            throw new InvalidOperationException(
                "Failed is valid only before the first-byte boundary.");
        }

        if (result.Outcome is PrintDispatchOutcome.Unknown &&
            attemptStatus is not ("Connecting" or "Dispatching"))
        {
            throw new InvalidOperationException("Unknown cannot complete this print attempt.");
        }
    }

    private static void AddLeaseParameters(SqliteCommand command, PrintJobLease lease)
    {
        command.Parameters.AddWithValue("$printJobId", lease.PrintJobId);
        command.Parameters.AddWithValue("$printerId", lease.PrinterId);
        command.Parameters.AddWithValue("$workerId", lease.WorkerId);
    }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();

    private static string RequireSha256(string value)
    {
        value = Require(value, nameof(value)).ToUpperInvariant();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The payload SHA-256 must contain 64 hexadecimal characters.", nameof(value));
        }

        return value;
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static async Task RollbackQuietlyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original operation or commit exception.
        }
    }
}

public sealed class SqlitePrintDispatchLifecycle : IPrintDispatchLifecycle
{
    private readonly SqlitePrintDispatchStore _store;
    private readonly PrintJobLease _lease;
    private readonly TimeProvider _timeProvider;
    private int _attemptStarted;

    public SqlitePrintDispatchLifecycle(
        SqlitePrintDispatchStore store,
        PrintJobLease lease,
        string printAttemptId,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        PrintAttemptId = string.IsNullOrWhiteSpace(printAttemptId)
            ? throw new ArgumentException("The print-attempt identifier cannot be empty.", nameof(printAttemptId))
            : printAttemptId.Trim();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string PrintAttemptId { get; }

    public async Task OnTransportAttemptStartingAsync(
        PrintDispatchRequest request,
        PrinterProfile profile,
        CancellationToken cancellationToken)
    {
        Validate(request, profile);
        if (Interlocked.CompareExchange(ref _attemptStarted, 1, 0) != 0)
        {
            throw new InvalidOperationException("The transport attempt was already started.");
        }

        try
        {
            await _store.StartAttemptAsync(
                _lease,
                PrintAttemptId,
                request.Payload.Sha256,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch
        {
            Volatile.Write(ref _attemptStarted, 0);
            throw;
        }
    }

    public Task OnFirstByteSendingAsync(
        PrintDispatchRequest request,
        PrinterProfile profile,
        CancellationToken cancellationToken)
    {
        Validate(request, profile);
        if (Volatile.Read(ref _attemptStarted) != 1)
        {
            throw new InvalidOperationException("The transport attempt has not been started.");
        }

        return _store.MarkFirstByteSendingAsync(
            _lease,
            PrintAttemptId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private void Validate(PrintDispatchRequest request, PrinterProfile profile)
    {
        if (!string.Equals(request.PrintJobId, _lease.PrintJobId, StringComparison.Ordinal) ||
            !string.Equals(request.BranchId, _lease.BranchId, StringComparison.Ordinal) ||
            !string.Equals(
                request.ProductionStationId,
                _lease.ProductionStationId,
                StringComparison.Ordinal) ||
            !string.Equals(profile.PrinterId, _lease.PrinterId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The dispatch request does not match the print-job lease.");
        }
    }
}
