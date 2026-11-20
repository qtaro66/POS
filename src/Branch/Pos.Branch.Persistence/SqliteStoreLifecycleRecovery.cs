using System.Text.Json;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record StoreLifecycleRequestTarget(string RequestId, string StoreSessionId, string ShiftId,
    StoreLifecycleAction Action, decimal? CashAmount);

public sealed record StoreLifecycleRequestStatus(string Status, StoreLifecycleResult? Result);

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<StoreLifecycleRequestStatus> ReadStoreLifecycleRequestAsync(string branchId, string deviceId,
        StoreLifecycleRequestTarget target, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ResolveStoreLifecycleRequestAsync(
                branchId, deviceId, target, null, cancellation), token);

    public Task<StoreLifecycleRequestStatus> RetireStoreLifecycleRequestAsync(string branchId, string deviceId,
        StoreLifecycleRequestTarget target, StoreOperationContext actor, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ResolveStoreLifecycleRequestAsync(
                branchId, deviceId, target, actor, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<StoreLifecycleRequestStatus> ResolveStoreLifecycleRequestAsync(string branchId,
        string deviceId, StoreLifecycleRequestTarget target, StoreOperationContext? actor, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.StoreSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ShiftId);
        if (!Enum.IsDefined(target.Action) || target.CashAmount < 0) { throw new ArgumentException("Invalid request target."); }
        if (actor is not null && actor.DeviceId != deviceId) { throw new ArgumentException("The actor must own the device."); }

        using var completed = CreateCommand("""
            SELECT command_json, result_json FROM store_lifecycle_commands
            WHERE branch_id = $branch AND request_id = $request;
            """);
        completed.Parameters.AddWithValue("$branch", branchId);
        completed.Parameters.AddWithValue("$request", target.RequestId);
        await using (var reader = await completed.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                var command = JsonSerializer.Deserialize<StoreLifecycleCommand>(reader.GetString(0))!;
                var original = new StoreLifecycleRequestTarget(command.RequestId, command.StoreSessionId,
                    command.ShiftId, command.Action, command.CashAmount);
                if (command.Context.DeviceId != deviceId || original != target)
                {
                    throw new InvalidOperationException("The request does not match the paired device and business command.");
                }
                return new("Completed", JsonSerializer.Deserialize<StoreLifecycleResult>(reader.GetString(1))!);
            }
        }

        using var retired = CreateCommand("""
            SELECT device_id, target_json FROM store_lifecycle_retirements
            WHERE branch_id = $branch AND request_id = $request;
            """);
        retired.Parameters.AddWithValue("$branch", branchId);
        retired.Parameters.AddWithValue("$request", target.RequestId);
        await using (var reader = await retired.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                if (reader.GetString(0) != deviceId ||
                    JsonSerializer.Deserialize<StoreLifecycleRequestTarget>(reader.GetString(1)) != target)
                {
                    throw new InvalidOperationException("The retired request does not match the paired device and business command.");
                }
                return new("Retired", null);
            }
        }
        if (actor is null) { return new("NotFound", null); }

        using var write = CreateCommand("""
            INSERT INTO store_lifecycle_retirements
                (branch_id,request_id,device_id,target_json,retired_by_employee_id,retired_at)
            VALUES ($branch,$request,$device,$target,$employee,$time);
            """);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$request", target.RequestId);
        write.Parameters.AddWithValue("$device", deviceId);
        write.Parameters.AddWithValue("$target", JsonSerializer.Serialize(target));
        write.Parameters.AddWithValue("$employee", actor.EmployeeId);
        write.Parameters.AddWithValue("$time", actor.OccurredAt.ToUniversalTime().ToString("O"));
        await write.ExecuteNonQueryAsync(token);
        return new("Retired", null);
    }
}
