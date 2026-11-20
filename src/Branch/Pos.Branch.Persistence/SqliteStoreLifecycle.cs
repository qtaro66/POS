using System.Text.Json;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public enum StoreLifecycleAction { OpenStore, OpenShift, CloseShift, CloseStore }

public sealed record StoreLifecycleCommand(string RequestId, string StoreSessionId, string ShiftId,
    StoreLifecycleAction Action, decimal? CashAmount, StoreOperationContext Context);

public sealed record StoreLifecycleResult(string StoreSessionId, string? ActiveShiftId, bool IsClosed,
    DateOnly BusinessDate, decimal OpeningCashAmount, ShiftClosing? Closing);

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<StoreLifecycleResult> ExecuteStoreLifecycleAsync(string branchId, StoreLifecycleCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).ExecuteStoreLifecycleAsync(branchId, command, token),
            cancellationToken);

    public Task<StoreLifecycleResult?> ReadCurrentStoreLifecycleAsync(string branchId,
        CancellationToken cancellationToken = default) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadCurrentStoreLifecycleAsync(branchId, token),
            cancellationToken);

    public Task<StoreLifecycleResult?> ReadStoreLifecycleAsync(string branchId, string storeSessionId,
        CancellationToken cancellationToken = default) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadStoreLifecycleAsync(branchId, storeSessionId, token),
            cancellationToken);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<StoreLifecycleResult?> ReadCurrentStoreLifecycleAsync(string branchId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        using var read = CreateCommand("""
            SELECT store_session_id FROM store_lifecycle_commands
            WHERE branch_id = $branch ORDER BY sequence DESC LIMIT 1;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        var storeId = await read.ExecuteScalarAsync(token) as string;
        return storeId is null ? null : await ReadStoreLifecycleAsync(branchId, storeId, token);
    }

    internal async Task<StoreLifecycleResult?> ReadStoreLifecycleAsync(string branchId, string storeSessionId,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeSessionId);
        var state = await LoadStoreLifecycleAsync(branchId, storeSessionId, token);
        return state is null ? null : Result(state);
    }

    internal async Task<StoreLifecycleResult> ExecuteStoreLifecycleAsync(string branchId,
        StoreLifecycleCommand command, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.StoreSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ShiftId);
        ArgumentNullException.ThrowIfNull(command.Context);
        var json = JsonSerializer.Serialize(command);
        using var prior = CreateCommand("""
            SELECT command_json, result_json FROM store_lifecycle_commands
            WHERE branch_id = $branch AND request_id = $request;
            """);
        prior.Parameters.AddWithValue("$branch", branchId);
        prior.Parameters.AddWithValue("$request", command.RequestId);
        await using (var reader = await prior.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                var original = JsonSerializer.Deserialize<StoreLifecycleCommand>(reader.GetString(0))!;
                if (original.RequestId != command.RequestId || original.StoreSessionId != command.StoreSessionId ||
                    original.ShiftId != command.ShiftId || original.Action != command.Action || original.CashAmount != command.CashAmount ||
                    original.Context.EmployeeId != command.Context.EmployeeId || original.Context.DeviceId != command.Context.DeviceId)
                {
                    throw new InvalidOperationException("The request identifier was already used for another command.");
                }
                return JsonSerializer.Deserialize<StoreLifecycleResult>(reader.GetString(1))!;
            }
        }

        var state = await LoadStoreLifecycleAsync(branchId, command.StoreSessionId, token);
        if (command.Action == StoreLifecycleAction.OpenStore)
        {
            if (state is not null) { throw new InvalidOperationException("The store identifier was already used."); }
            if (command.CashAmount is null) { throw new ArgumentException("Opening cash is required."); }
            state = StoreSession.OpenStore(command.StoreSessionId, command.ShiftId, command.CashAmount.Value, command.Context);
            using var insert = CreateCommand("""
                INSERT INTO store_lifecycle_sessions(branch_id,store_session_id,is_closed)
                VALUES ($branch,$store,0);
                """);
            insert.Parameters.AddWithValue("$branch", branchId);
            insert.Parameters.AddWithValue("$store", command.StoreSessionId);
            await insert.ExecuteNonQueryAsync(token);
        }
        else
        {
            if (state is null) { throw new InvalidOperationException("The store does not exist."); }
            if (command.Action == StoreLifecycleAction.OpenShift)
            {
                state.OpenShift(command.ShiftId, command.CashAmount, command.Context);
            }
            else
            {
                if (state.ActiveShiftId != command.ShiftId)
                {
                    throw new InvalidOperationException("The command must target the active shift.");
                }
                var applied = await ReadStoreAppliedCashAsync(branchId, command.StoreSessionId, token);
                using var partial = CreateCommand("""
                    SELECT EXISTS(SELECT 1 FROM table_sessions
                        WHERE branch_id = $branch AND status = 'PartiallyPaid');
                    """);
                partial.Parameters.AddWithValue("$branch", branchId);
                var hasPartial = (long)(await partial.ExecuteScalarAsync(token))! != 0;
                switch (command.Action)
                {
                    case StoreLifecycleAction.CloseShift:
                        state.CloseShift(applied, command.CashAmount, hasPartial, command.Context);
                        break;
                    case StoreLifecycleAction.CloseStore:
                        if (command.CashAmount is null) { throw new ArgumentException("A store closing count is required."); }
                        state.CloseStore(applied, command.CashAmount.Value, hasPartial, command.Context);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(command));
                }
            }
        }

        var result = Result(state);
        using var write = CreateCommand("""
            UPDATE store_lifecycle_sessions SET is_closed = $closed
                WHERE branch_id = $branch AND store_session_id = $store;
            INSERT INTO store_lifecycle_commands(branch_id,request_id,store_session_id,command_json,result_json)
                VALUES ($branch,$request,$store,$command,$result);
            """);
        write.Parameters.AddWithValue("$closed", state.IsClosed ? 1 : 0);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$store", command.StoreSessionId);
        write.Parameters.AddWithValue("$request", command.RequestId);
        write.Parameters.AddWithValue("$command", json);
        write.Parameters.AddWithValue("$result", JsonSerializer.Serialize(result));
        await write.ExecuteNonQueryAsync(token);
        return result;
    }

    private async Task<decimal> ReadStoreAppliedCashAsync(string branchId, string storeId, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT result_json FROM cash_sales
            WHERE branch_id = $branch AND json_extract(result_json, '$.StoreSessionId') = $store;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$store", storeId);
        await using var reader = await read.ExecuteReaderAsync(token);
        decimal total = 0;
        while (await reader.ReadAsync(token))
        {
            total += JsonSerializer.Deserialize<CashSaleResult>(reader.GetString(0))!.AppliedAmount;
        }
        return total;
    }

    private async Task<StoreSession?> LoadStoreLifecycleAsync(string branchId, string storeId, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT command_json,result_json FROM store_lifecycle_commands
            WHERE branch_id = $branch AND store_session_id = $store ORDER BY sequence;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$store", storeId);
        await using var reader = await read.ExecuteReaderAsync(token);
        StoreSession? state = null;
        while (await reader.ReadAsync(token))
        {
            var command = JsonSerializer.Deserialize<StoreLifecycleCommand>(reader.GetString(0))!;
            var result = JsonSerializer.Deserialize<StoreLifecycleResult>(reader.GetString(1))!;
            switch (command.Action)
            {
                case StoreLifecycleAction.OpenStore:
                    state = StoreSession.OpenStore(storeId, command.ShiftId, command.CashAmount!.Value, command.Context);
                    break;
                case StoreLifecycleAction.OpenShift:
                    state!.OpenShift(command.ShiftId, command.CashAmount, command.Context);
                    break;
                case StoreLifecycleAction.CloseShift:
                    state!.CloseShift(result.Closing!.ExpectedCashAmount - state.OpeningCashAmount,
                        command.CashAmount, false, command.Context);
                    break;
                case StoreLifecycleAction.CloseStore:
                    state!.CloseStore(result.Closing!.ExpectedCashAmount - state.OpeningCashAmount,
                        command.CashAmount!.Value, false, command.Context);
                    break;
            }
        }
        return state;
    }

    private static StoreLifecycleResult Result(StoreSession state) =>
        new(state.Id, state.ActiveShiftId, state.IsClosed, state.BusinessDate, state.OpeningCashAmount,
            state.Closings.LastOrDefault());
}
