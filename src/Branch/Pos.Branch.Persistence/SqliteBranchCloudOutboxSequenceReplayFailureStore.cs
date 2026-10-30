using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxSequenceReplayFailureStore :
    IBranchCloudOutboxSequenceReplayFailureStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxSequenceReplayFailureStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The database path cannot be empty.",
                nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<BranchCloudOutboxDeliveryFailureResult> RecordAsync(
        BranchCloudOutboxSequenceReplayFailure failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        var replay = await ReadReplayAsync(
            connection,
            transaction,
            failure,
            cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var retryCount = await UpdateReplayLeaseAsync(
            connection,
            transaction,
            failure,
            cancellationToken);
        await InsertFailureAsync(
            connection,
            transaction,
            failure,
            retryCount,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BranchCloudOutboxDeliveryFailureResult(
            retryCount,
            alreadyRecorded: false);
    }

    private static async Task<BranchCloudOutboxDeliveryFailureResult?> ReadReplayAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxSequenceReplayFailure failure,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT recovery_id, branch_id, outbox_id, branch_sequence, lease_owner,
                   claimed_at, lease_expires_at, failed_at, next_retry_at,
                   error_code, retry_count_after, expected_branch_sequence
            FROM cloud_sync_sequence_replay_failures
            WHERE failure_id = $failureId;
            """;
        command.Parameters.AddWithValue("$failureId", failure.FailureId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var lease = failure.Lease;
        var matches =
            string.Equals(reader.GetString(0), lease.RecoveryId, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(1), lease.BranchId, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(2), lease.OutboxId, StringComparison.Ordinal) &&
            reader.GetInt64(3) == lease.BranchSequence &&
            string.Equals(reader.GetString(4), lease.LeaseOwner, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(5), FormatTimestamp(lease.ClaimedAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(6), FormatTimestamp(lease.LeaseExpiresAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(7), FormatTimestamp(failure.FailedAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(8), FormatTimestamp(failure.NextRetryAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(9), failure.ErrorCode, StringComparison.Ordinal) &&
            ReadNullableInt64(reader, 11) == failure.ExpectedBranchSequence;
        if (!matches)
        {
            throw new InvalidOperationException(
                "The Cloud delivery failure identifier is already used by another result.");
        }

        return new BranchCloudOutboxDeliveryFailureResult(
            reader.GetInt32(10),
            alreadyRecorded: true);
    }

    private static async Task<int> UpdateReplayLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxSequenceReplayFailure failure,
        CancellationToken cancellationToken)
    {
        var lease = failure.Lease;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cloud_sync_sequence_replay_leases
            SET retry_count = retry_count + 1,
                next_retry_at = $nextRetryAt,
                lease_owner = NULL,
                claimed_at = NULL,
                lease_expires_at = NULL
            WHERE recovery_id = $recoveryId
              AND lease_owner = $leaseOwner
              AND claimed_at = $claimedAt
              AND lease_expires_at = $leaseExpiresAt
              AND EXISTS (
                  SELECT 1 FROM cloud_sync_sequence_replay_requests r
                  WHERE r.recovery_id = cloud_sync_sequence_replay_leases.recovery_id
                    AND r.branch_id = $branchId
                    AND r.expected_outbox_id = $outboxId
                    AND r.expected_branch_sequence = $branchSequence
                    AND r.completed_at IS NULL)
            RETURNING retry_count;
            """;
        AddFailureParameters(command, failure);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long retryCount
            ? checked((int)retryCount)
            : throw new InvalidOperationException(
                "The replay lease is replaced, completed, or missing.");
    }

    private static async Task InsertFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxSequenceReplayFailure failure,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var lease = failure.Lease;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cloud_sync_sequence_replay_failures (
                failure_id, recovery_id, branch_id, outbox_id, branch_sequence,
                lease_owner, claimed_at, lease_expires_at, failed_at,
                next_retry_at, error_code, retry_count_after,
                expected_branch_sequence)
            VALUES (
                $failureId, $recoveryId, $branchId, $outboxId, $branchSequence,
                $leaseOwner, $claimedAt, $leaseExpiresAt, $failedAt,
                $nextRetryAt, $errorCode, $retryCount,
                $expectedBranchSequence);
            """;
        AddFailureParameters(command, failure);
        command.Parameters.AddWithValue("$retryCount", retryCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddFailureParameters(
        SqliteCommand command,
        BranchCloudOutboxSequenceReplayFailure failure)
    {
        var lease = failure.Lease;
        command.Parameters.AddWithValue("$failureId", failure.FailureId);
        command.Parameters.AddWithValue("$recoveryId", lease.RecoveryId);
        command.Parameters.AddWithValue("$branchId", lease.BranchId);
        command.Parameters.AddWithValue("$outboxId", lease.OutboxId);
        command.Parameters.AddWithValue("$branchSequence", lease.BranchSequence);
        command.Parameters.AddWithValue("$leaseOwner", lease.LeaseOwner);
        command.Parameters.AddWithValue(
            "$claimedAt",
            FormatTimestamp(lease.ClaimedAt));
        command.Parameters.AddWithValue(
            "$leaseExpiresAt",
            FormatTimestamp(lease.LeaseExpiresAt));
        command.Parameters.AddWithValue(
            "$failedAt",
            FormatTimestamp(failure.FailedAt));
        command.Parameters.AddWithValue(
            "$nextRetryAt",
            FormatTimestamp(failure.NextRetryAt));
        command.Parameters.AddWithValue("$errorCode", failure.ErrorCode);
        command.Parameters.AddWithValue(
            "$expectedBranchSequence",
            failure.ExpectedBranchSequence is { } expected
                ? expected
                : DBNull.Value);
    }

    private static long? ReadNullableInt64(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
