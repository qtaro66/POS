using System.Text.Json;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private async Task CaptureClosingReportLifecycleAsync(string branchId, StoreLifecycleCommand command,
        StoreSession state, CancellationToken token)
    {
        var opened = state.Operations.Where(operation => operation.Action is "OpenStore" or "OpenShift").ToArray();
        if (opened.Length == 0 || opened[0].Action != "OpenStore" || state.ActiveShiftId is not null ||
            state.Closings.Count != opened.Length)
        {
            throw new InvalidDataException("Closing report lifecycle is incomplete.");
        }
        var shifts = opened.Select(operation => new ClosingReportShiftContext(operation.ShiftId, operation.CashAmount,
            operation.Context, state.Closings.Single(close => close.ShiftId == operation.ShiftId))).ToArray();
        var context = new ClosingReportLifecycleContext(1, branchId, command.RequestId, state.Id, command.ShiftId,
            state.BusinessDate, state.OpeningCashAmount, opened[0].Context, state.IsClosed, shifts);
        ValidateClosingReportLifecycle(context, branchId, command.RequestId, state.Closings[^1], state.BusinessDate);
        using var write = CreateCommand("""
            INSERT INTO closing_report_lifecycle_snapshots(branch_id,request_id,store_session_id,shift_id,context_json)
                VALUES ($branch,$request,$store,$shift,$context);
            """);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$request", command.RequestId);
        write.Parameters.AddWithValue("$store", command.StoreSessionId);
        write.Parameters.AddWithValue("$shift", command.ShiftId);
        write.Parameters.AddWithValue("$context", JsonSerializer.Serialize(context));
        await write.ExecuteNonQueryAsync(token);
    }

    private async Task<ClosingReportLifecycleContext?> ReadSavedClosingReportLifecycleAsync(string branchId,
        string requestId, ClosingReportSnapshot report, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT store_session_id,shift_id,context_json FROM closing_report_lifecycle_snapshots
            WHERE branch_id=$branch AND request_id=$request;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$request", requestId);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { return null; }
        if (reader.GetString(0) != report.StoreSessionId || reader.GetString(1) != report.ShiftId)
        {
            throw new InvalidDataException("Saved closing report lifecycle identity is inconsistent.");
        }
        var context = JsonSerializer.Deserialize<ClosingReportLifecycleContext>(reader.GetString(2))
            ?? throw new InvalidDataException("Saved closing report lifecycle is unavailable.");
        ValidateClosingReportLifecycle(context, branchId, requestId, report.Closing, report.BusinessDate);
        return context;
    }

    private static void ValidateClosingReportLifecycle(ClosingReportLifecycleContext context, string branchId,
        string requestId, ShiftClosing final, DateOnly businessDate)
    {
        static bool Identity(string? value) => !string.IsNullOrWhiteSpace(value);
        static bool Attribution(StoreOperationContext? value) => value is not null && Identity(value.EmployeeId) &&
            Identity(value.DeviceId) && value.OccurredAt.Offset == TimeSpan.Zero;
        static bool Money(decimal? value) => value is null || value >= 0;
        if (context.SchemaVersion != 1 || context.BranchId != branchId || context.RequestId != requestId ||
            context.StoreSessionId != final.StoreSessionId || context.ClosingShiftId != final.ShiftId ||
            context.BusinessDate != businessDate || context.ClosesStore != final.ClosesStore ||
            !Money(context.OpeningCashAmount) || !Attribution(context.Opened) || context.Opened.BranchLocalDate != businessDate ||
            context.Shifts is null || context.Shifts.Count == 0 ||
            context.Shifts.Any(shift => shift is null || !Identity(shift.ShiftId)) ||
            context.Shifts.Select(shift => shift.ShiftId).Distinct(StringComparer.Ordinal).Count() != context.Shifts.Count ||
            context.Shifts[0].Opened != context.Opened || context.Shifts[0].OpeningCashAmount != context.OpeningCashAmount ||
            context.Shifts[^1].ShiftId != context.ClosingShiftId || context.Shifts[^1].Closing != final)
        {
            throw new InvalidDataException("Saved closing report lifecycle is inconsistent.");
        }
        for (var index = 0; index < context.Shifts.Count; index++)
        {
            var shift = context.Shifts[index];
            var close = shift.Closing;
            if (!Money(shift.OpeningCashAmount) || !Attribution(shift.Opened) || shift.Opened.BranchLocalDate != businessDate || close is null ||
                close.StoreSessionId != context.StoreSessionId || close.ShiftId != shift.ShiftId ||
                close.ClosesStore != (context.ClosesStore && index == context.Shifts.Count - 1) ||
                !Attribution(close.Context) || close.Context.BranchLocalDate != businessDate || !Money(close.ExpectedCashAmount) || !Money(close.ActualCashAmount) ||
                (close.ClosesStore && close.ActualCashAmount is null) || close.CashDifference != close.ActualCashAmount - close.ExpectedCashAmount)
            {
                throw new InvalidDataException("Saved closing report shift lifecycle is inconsistent.");
            }
        }
    }
}
