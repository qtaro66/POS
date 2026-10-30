using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxSequenceReplayStore :
    IBranchCloudOutboxSequenceReplayStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxSequenceReplayStore(string databasePath)
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

    public async Task<BranchCloudOutboxSequenceReplayResult> RequestAsync(
        BranchCloudOutboxSequenceReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        if (await ReadExistingAsync(
                connection,
                transaction,
                request,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return BranchCloudOutboxSequenceReplayResult.AlreadyRequested;
        }

        var expectedOutboxId = await ReadExpectedOutboxIdAsync(
            connection,
            transaction,
            request,
            cancellationToken);
        if (expectedOutboxId is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return BranchCloudOutboxSequenceReplayResult.ExpectedSequenceNotFound;
        }

        if (await HasActiveRequestAsync(
                connection,
                transaction,
                request,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return BranchCloudOutboxSequenceReplayResult.AlreadyRequested;
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO cloud_sync_sequence_replay_requests (
                recovery_id, branch_id, expected_branch_sequence,
                received_branch_sequence, expected_outbox_id, requested_at)
            VALUES (
                $recoveryId, $branchId, $expectedBranchSequence,
                $receivedBranchSequence, $expectedOutboxId, $requestedAt);
            """;
        AddRequestParameters(insert, request);
        insert.Parameters.AddWithValue("$expectedOutboxId", expectedOutboxId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BranchCloudOutboxSequenceReplayResult.Requested;
    }

    private static async Task<bool> ReadExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxSequenceReplayRequest request,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT branch_id, expected_branch_sequence,
                   received_branch_sequence, requested_at
            FROM cloud_sync_sequence_replay_requests
            WHERE recovery_id = $recoveryId;
            """;
        command.Parameters.AddWithValue("$recoveryId", request.RecoveryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        var matches =
            string.Equals(
                reader.GetString(0),
                request.BranchId,
                StringComparison.Ordinal) &&
            reader.GetInt64(1) == request.ExpectedBranchSequence &&
            reader.GetInt64(2) == request.ReceivedBranchSequence &&
            string.Equals(
                reader.GetString(3),
                FormatTimestamp(request.RequestedAt),
                StringComparison.Ordinal);
        if (!matches)
        {
            throw new InvalidOperationException(
                "The Cloud sequence-recovery identifier is already used by another request.");
        }

        return true;
    }

    private static async Task<string?> ReadExpectedOutboxIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxSequenceReplayRequest request,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT outbox_id
            FROM cloud_sync_outbox
            WHERE branch_id = $branchId
              AND branch_sequence = $expectedBranchSequence;
            """;
        command.Parameters.AddWithValue("$branchId", request.BranchId);
        command.Parameters.AddWithValue(
            "$expectedBranchSequence",
            request.ExpectedBranchSequence);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<bool> HasActiveRequestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BranchCloudOutboxSequenceReplayRequest request,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM cloud_sync_sequence_replay_requests
                WHERE branch_id = $branchId
                  AND expected_branch_sequence = $expectedBranchSequence
                  AND completed_at IS NULL);
            """;
        command.Parameters.AddWithValue("$branchId", request.BranchId);
        command.Parameters.AddWithValue(
            "$expectedBranchSequence",
            request.ExpectedBranchSequence);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static void AddRequestParameters(
        SqliteCommand command,
        BranchCloudOutboxSequenceReplayRequest request)
    {
        command.Parameters.AddWithValue("$recoveryId", request.RecoveryId);
        command.Parameters.AddWithValue("$branchId", request.BranchId);
        command.Parameters.AddWithValue(
            "$expectedBranchSequence",
            request.ExpectedBranchSequence);
        command.Parameters.AddWithValue(
            "$receivedBranchSequence",
            request.ReceivedBranchSequence);
        command.Parameters.AddWithValue(
            "$requestedAt",
            FormatTimestamp(request.RequestedAt));
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
