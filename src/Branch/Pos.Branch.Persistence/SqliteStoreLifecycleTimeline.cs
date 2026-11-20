using System.Globalization;
using System.Text.Json;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record StoreShiftTimelineEntry(string StoreSessionId, string ShiftId, decimal? OpeningCashAmount,
    StoreOperationContext Opened, ShiftClosing? Closing, StoreCashSummary? CashSummary = null);

public sealed record StoreSessionTimelineEntry(string StoreSessionId, DateOnly BusinessDate, decimal OpeningCashAmount,
    StoreOperationContext Opened, bool IsClosed, IReadOnlyList<StoreShiftTimelineEntry> Shifts);

public sealed record StoreLifecycleWorkspace(string BranchId, DateOnly BusinessDate, DateTimeOffset AsOf,
    StoreLifecycleResult? Current, IReadOnlyList<StoreSessionTimelineEntry> Stores, StoreCashSummary? CashSummary = null);

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<StoreLifecycleWorkspace> ReadStoreLifecycleWorkspaceAsync(string branchId, DateOnly businessDate,
        DateTimeOffset asOf, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadStoreLifecycleWorkspaceAsync(
                branchId, businessDate, asOf, cancellation), token);

    public Task<StoreShiftTimelineEntry?> ReadStoreShiftClosingAsync(string branchId, string storeSessionId,
        string shiftId, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadStoreShiftClosingAsync(
                branchId, storeSessionId, shiftId, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<StoreLifecycleWorkspace> ReadStoreLifecycleWorkspaceAsync(string branchId,
        DateOnly businessDate, DateTimeOffset asOf, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        var current = await ReadCurrentStoreLifecycleAsync(branchId, token);
        using var read = CreateCommand("""
            SELECT history.command_json,history.result_json,snapshot.summary_json FROM store_lifecycle_commands history
            LEFT JOIN store_lifecycle_cash_snapshots snapshot ON snapshot.branch_id = history.branch_id AND snapshot.request_id = history.request_id
            WHERE history.branch_id = $branch AND json_extract(history.command_json, '$.Context.BranchLocalDate') = $date
            ORDER BY history.sequence;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$date", businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var commands = await ReadTimelineCommandsAsync(read, token);
        var stores = commands.GroupBy(entry => entry.Command.StoreSessionId).Select(group =>
        {
            var opening = group.First().Command;
            var latest = group.Last().Result;
            return new StoreSessionTimelineEntry(opening.StoreSessionId, latest.BusinessDate,
                latest.OpeningCashAmount, opening.Context, latest.IsClosed, BuildShiftTimeline(group).ToArray());
        }).ToArray();
        var summary = current is not null && !current.IsClosed
            ? await ReadStoreCashSummaryAsync(branchId, current.StoreSessionId, current.ActiveShiftId, token) : null;
        return new(branchId, businessDate, asOf.ToUniversalTime(), current, stores, summary);
    }

    internal async Task<StoreShiftTimelineEntry?> ReadStoreShiftClosingAsync(string branchId,
        string storeSessionId, string shiftId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(shiftId);
        using var read = CreateCommand("""
            SELECT history.command_json,history.result_json,snapshot.summary_json FROM store_lifecycle_commands history
            LEFT JOIN store_lifecycle_cash_snapshots snapshot ON snapshot.branch_id = history.branch_id AND snapshot.request_id = history.request_id
            WHERE history.branch_id = $branch AND history.store_session_id = $store
                AND json_extract(command_json, '$.ShiftId') = $shift
            ORDER BY history.sequence;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$store", storeSessionId);
        read.Parameters.AddWithValue("$shift", shiftId);
        var commands = await ReadTimelineCommandsAsync(read, token);
        return BuildShiftTimeline(commands).SingleOrDefault(entry => entry.Closing is not null);
    }

    private static IEnumerable<StoreShiftTimelineEntry> BuildShiftTimeline(IEnumerable<TimelineCommand> commands) =>
        commands.GroupBy(entry => entry.Command.ShiftId).Select(group =>
        {
            var opening = group.First().Command;
            var closing = group.LastOrDefault(entry =>
                entry.Command.Action is StoreLifecycleAction.CloseShift or StoreLifecycleAction.CloseStore);
            return new StoreShiftTimelineEntry(opening.StoreSessionId, opening.ShiftId,
                opening.CashAmount, opening.Context, closing?.Result.Closing, closing?.CashSummary);
        });

    private static async Task<List<TimelineCommand>> ReadTimelineCommandsAsync(
        Microsoft.Data.Sqlite.SqliteCommand command, CancellationToken token)
    {
        var commands = new List<TimelineCommand>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            commands.Add(new(JsonSerializer.Deserialize<StoreLifecycleCommand>(reader.GetString(0))!,
                JsonSerializer.Deserialize<StoreLifecycleResult>(reader.GetString(1))!,
                reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<StoreCashSummary>(reader.GetString(2))));
        }
        return commands;
    }

    private sealed record TimelineCommand(StoreLifecycleCommand Command, StoreLifecycleResult Result, StoreCashSummary? CashSummary);
}
