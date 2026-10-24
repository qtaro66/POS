using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudDeliveryConfirmationStore :
    IBranchCloudDeliveryConfirmationStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudDeliveryConfirmationStore(string databasePath)
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

    public async Task<BranchCloudDeliveryConfirmationResult> ConfirmAsync(
        BranchCloudDeliveryConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cloud_sync_outbox
                SET processing_status = 'Completed',
                    next_retry_at = NULL,
                    last_error = NULL,
                    successfully_sent_at = $acknowledgedAt
                WHERE branch_id = $branchId
                  AND outbox_id = $outboxId
                  AND successfully_sent_at IS NULL;
                """;
            update.Parameters.AddWithValue("$branchId", confirmation.BranchId);
            update.Parameters.AddWithValue("$outboxId", confirmation.OutboxId);
            update.Parameters.AddWithValue(
                "$acknowledgedAt",
                FormatTimestamp(confirmation.AcknowledgedAt));
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return BranchCloudDeliveryConfirmationResult.Confirmed;
            }
        }

        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT successfully_sent_at
            FROM cloud_sync_outbox
            WHERE branch_id = $branchId
              AND outbox_id = $outboxId;
            """;
        read.Parameters.AddWithValue("$branchId", confirmation.BranchId);
        read.Parameters.AddWithValue("$outboxId", confirmation.OutboxId);
        var stored = await read.ExecuteScalarAsync(cancellationToken);
        var result = stored is string
            ? BranchCloudDeliveryConfirmationResult.AlreadyConfirmed
            : BranchCloudDeliveryConfirmationResult.NotFound;
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
