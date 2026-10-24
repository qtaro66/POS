using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxDeliveryFailureStore :
    IBranchCloudOutboxDeliveryFailureStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxDeliveryFailureStore(string databasePath)
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
        BranchCloudOutboxDeliveryFailure failure,
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

        var retryCount = await UpdateOutboxAsync(
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
        BranchCloudOutboxDeliveryFailure failure,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT branch_id, outbox_id, branch_sequence, lease_owner,
                   claimed_at, lease_expires_at, failed_at, next_retry_at,
                   error_code, retry_count_after
            FROM cloud_sync_delivery_failures
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
            string.Equals(reader.GetString(0), lease.BranchId, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(1), lease.OutboxId, StringComparison.Ordinal) &&
            reader.GetInt64(2) == lease.BranchSequence &&
            string.Equals(reader.GetString(3), lease.LeaseOwner, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(4), FormatTimestamp(lease.ClaimedAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(5), FormatTimestamp(lease.LeaseExpiresAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(6), FormatTimestamp(failure.FailedAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(7), FormatTimestamp(failure.NextRetryAt), StringComparison.Ordinal) &&
            string.Equals(reader.GetString(8), failure.ErrorCode, StringComparison.Ordinal);
        if (!matches)
        {
            throw new InvalidOperationException(
                "The Cloud delivery failure identifier is already used by another result.");
        }

        return new BranchCloudOutboxDeliveryFailureResult(
            reader.GetInt32(9),
            alreadyRecorded: true);
    }

    private static async Task<int> UpdateOutboxAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxDeliveryFailure failure,
        CancellationToken cancellationToken)
    {
        var lease = failure.Lease;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cloud_sync_outbox
            SET processing_status = 'Failed',
                retry_count = retry_count + 1,
                next_retry_at = $nextRetryAt,
                last_error = $errorCode,
                lease_owner = NULL,
                claimed_at = NULL,
                lease_expires_at = NULL
            WHERE branch_id = $branchId
              AND outbox_id = $outboxId
              AND branch_sequence = $branchSequence
              AND successfully_sent_at IS NULL
              AND lease_owner = $leaseOwner
              AND claimed_at = $claimedAt
              AND lease_expires_at = $leaseExpiresAt
              AND julianday(lease_expires_at) > julianday($failedAt)
            RETURNING retry_count;
            """;
        AddFailureParameters(command, failure);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long retryCount
            ? checked((int)retryCount)
            : throw new InvalidOperationException(
                "The Cloud outbox lease is expired, replaced, confirmed, or missing.");
    }

    private static async Task InsertFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxDeliveryFailure failure,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var lease = failure.Lease;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cloud_sync_delivery_failures (
                failure_id, branch_id, outbox_id, branch_sequence,
                lease_owner, claimed_at, lease_expires_at, failed_at,
                next_retry_at, error_code, retry_count_after)
            VALUES (
                $failureId, $branchId, $outboxId, $branchSequence,
                $leaseOwner, $claimedAt, $leaseExpiresAt, $failedAt,
                $nextRetryAt, $errorCode, $retryCount);
            """;
        AddFailureParameters(command, failure);
        command.Parameters.AddWithValue("$retryCount", retryCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddFailureParameters(
        SqliteCommand command,
        BranchCloudOutboxDeliveryFailure failure)
    {
        var lease = failure.Lease;
        command.Parameters.AddWithValue("$failureId", failure.FailureId);
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
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
