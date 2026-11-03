using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public enum SoldOutAdjustmentResult { Applied, Replayed, Conflict, ItemNotFound }

public sealed record SoldOutAdjustment(
    string OperationId, string BranchId, string SellableItemId, string EmployeeId, string DeviceId,
    bool ExpectedSoldOut, bool SoldOut, DateTimeOffset OccurredAt);

// The caller must authorize the employee and device before invoking this store.
public sealed class SqliteBranchSoldOutAdjustmentStore(string databasePath)
{
    public async Task<SoldOutAdjustmentResult> ApplyAsync(SoldOutAdjustment adjustment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjustment.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjustment.BranchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjustment.SellableItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjustment.EmployeeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjustment.DeviceId);
        if (adjustment.ExpectedSoldOut == adjustment.SoldOut)
        {
            throw new ArgumentException("A Sold Out adjustment must change the expected state.", nameof(adjustment));
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$operation", adjustment.OperationId);
        command.Parameters.AddWithValue("$branch", adjustment.BranchId);
        command.Parameters.AddWithValue("$item", adjustment.SellableItemId);
        command.Parameters.AddWithValue("$employee", adjustment.EmployeeId);
        command.Parameters.AddWithValue("$device", adjustment.DeviceId);
        command.Parameters.AddWithValue("$before", adjustment.ExpectedSoldOut ? 1 : 0);
        command.Parameters.AddWithValue("$after", adjustment.SoldOut ? 1 : 0);
        command.Parameters.AddWithValue("$at", adjustment.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        command.CommandText = """
            SELECT branch_id = $branch AND sellable_item_id = $item AND employee_id = $employee
                AND device_id = $device AND previous_sold_out = $before AND current_sold_out = $after
            FROM branch_sold_out_audit WHERE audit_id = $operation;
            """;
        if (await command.ExecuteScalarAsync(cancellationToken) is long matches)
        {
            return matches == 1 ? SoldOutAdjustmentResult.Replayed : SoldOutAdjustmentResult.Conflict;
        }

        command.CommandText = """
            SELECT coalesce((SELECT is_sold_out FROM branch_sold_out_overrides
                WHERE branch_id = $branch AND sellable_item_id = $item), 0);
            """;
        var current = (long)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
        if (current != adjustment.ExpectedSoldOut) { return SoldOutAdjustmentResult.Conflict; }

        if (adjustment.SoldOut)
        {
            command.CommandText = """
                SELECT 1 FROM master_data_sellable_items AS item
                JOIN master_data_packages AS package ON package.branch_id = item.branch_id
                    AND package.master_data_version = item.master_data_version
                WHERE item.branch_id = $branch AND item.sellable_item_id = $item AND package.lifecycle_status = 'Active';
                """;
            if (await command.ExecuteScalarAsync(cancellationToken) is null) { return SoldOutAdjustmentResult.ItemNotFound; }
        }

        command.CommandText = """
            INSERT INTO branch_sold_out_overrides
                (branch_id, sellable_item_id, is_sold_out, changed_by_employee_id, changed_by_device_id, changed_at)
            VALUES ($branch, $item, $after, $employee, $device, $at)
            ON CONFLICT (branch_id, sellable_item_id) DO UPDATE SET is_sold_out = excluded.is_sold_out,
                changed_by_employee_id = excluded.changed_by_employee_id, changed_by_device_id = excluded.changed_by_device_id,
                changed_at = excluded.changed_at;
            INSERT INTO branch_sold_out_audit
                (audit_id, branch_id, sellable_item_id, previous_sold_out, current_sold_out, employee_id, device_id, occurred_at)
            VALUES ($operation, $branch, $item, $before, $after, $employee, $device, $at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SoldOutAdjustmentResult.Applied;
    }
}
