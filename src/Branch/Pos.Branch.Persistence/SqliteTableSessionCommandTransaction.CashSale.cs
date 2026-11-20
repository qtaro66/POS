using System.Text.Json;
using Pos.Application.TableSessions;
using Pos.Domain.Payments;
using Pos.Domain.StoreSessions;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<CashSaleResult?> CompleteCashSaleAsync(string branchId, string sessionId, string requestId,
        string employeeId, string deviceId, DateTimeOffset now, long expectedVersion, decimal tendered, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT result_json FROM cash_sales WHERE branch_id = $branch AND request_id = $request;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        read.Parameters.AddWithValue("$request", requestId);
        if (await read.ExecuteScalarAsync(token) is string previous)
        {
            var result = JsonSerializer.Deserialize<CashSaleResult>(previous)!;
            return result.TableSessionId == sessionId && result.EmployeeId == employeeId && result.DeviceId == deviceId &&
                result.TenderedAmount == tendered && result.ExpectedVersion == expectedVersion ? result : null;
        }

        if (await FindReceiptAsync(branchId, requestId, token) is not null) { return null; }

        var shift = await ResolveCashLifecycleAsync(branchId, employeeId, deviceId, now, token);
        if (shift is null) { return null; }
        read.CommandText = """
            SELECT preview.content_json
            FROM table_sessions session
            JOIN bill_preview_snapshots preview ON preview.branch_id = session.branch_id
                AND preview.table_session_id = session.table_session_id
            WHERE session.branch_id = $branch AND session.table_session_id = $session
                AND session.status = 'PaymentPending' AND session.version = $version
            ORDER BY preview.created_at DESC, preview.snapshot_id DESC LIMIT 1;
            """;
        read.Parameters.AddWithValue("$session", sessionId);
        read.Parameters.AddWithValue("$version", expectedVersion);
        CashSaleResult sale;
        string previewJson;
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) { return null; }
            previewJson = reader.GetString(0);
            using var preview = JsonDocument.Parse(previewJson);
            var due = preview.RootElement.GetProperty("netTotal").GetDecimal();
            if (tendered < due) { return null; }
            var cash = new CashSettlement(due, tendered);
            sale = new(sessionId, employeeId, deviceId, expectedVersion, shift.ShiftId, shift.StoreId, shift.BusinessDate, cash.TenderedAmount, cash.AppliedAmount, cash.ChangeAmount, "TEST-" + Guid.NewGuid().ToString("N"), "QueuedForSimulation");
        }

        var session = await FindSessionAsync(branchId, TableSessionId.From(sessionId), token);
        if (session is null || session.Status != TableSessionStatus.PaymentPending || session.Version != expectedVersion)
        {
            return null;
        }

        var context = new TableSessionTransitionContext(employeeId, deviceId, now);
        session.StartPayment(context);
        await UpdateSessionAsync(branchId, session, expectedVersion, token);
        session.CompletePayment(fullyApplied: true, context);
        await UpdateSessionAsync(branchId, session, expectedVersion + 1, token);

        using var write = CreateCommand("""
            INSERT INTO cash_sales (branch_id, request_id, table_session_id, result_json, bill_snapshot_json)
            VALUES ($branch, $request, $session, $result, $preview);
            INSERT INTO simulated_cash_receipt_jobs VALUES ($branch, $request, $receipt, $now);
            """);
        write.Parameters.AddWithValue("$branch", branchId);
        write.Parameters.AddWithValue("$request", requestId);
        write.Parameters.AddWithValue("$session", sessionId);
        write.Parameters.AddWithValue("$result", JsonSerializer.Serialize(sale));
        write.Parameters.AddWithValue("$preview", previewJson);
        write.Parameters.AddWithValue("$receipt", sale.ReceiptNumber);
        write.Parameters.AddWithValue("$now", FormatTimestamp(now));
        await write.ExecuteNonQueryAsync(token);
        await AddAuditAsync(new($"{branchId}:{requestId}:audit:1", branchId, requestId, session.Transitions[^1]), token);
        await AddCloudOutboxAsync(new($"{branchId}:{requestId}:cloud:1", branchId, requestId,
            session.Id, TableSessionAction.CompletePayment, session.Version, now), token);
        return sale;
    }

    private sealed record CashLifecycle(string ShiftId, string StoreId, string BusinessDate);

    private async Task<CashLifecycle?> ResolveCashLifecycleAsync(string branchId, string employeeId, string deviceId,
        DateTimeOffset now, CancellationToken token)
    {
        var current = await ReadCurrentStoreLifecycleAsync(branchId, token);
        if (current is not null)
        {
            if (current.IsClosed || current.ActiveShiftId is null) { return null; }
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
            var store = await LoadStoreLifecycleAsync(branchId, current.StoreSessionId, token);
            try
            {
                store!.EnsureCanCompletePayment(new StoreOperationContext(employeeId, deviceId, now, localDate));
            }
            catch (InvalidOperationException) { return null; }
            return new(current.ActiveShiftId, current.StoreSessionId,
                current.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }

        using var read = CreateCommand("""
            SELECT shift_id, store_session_id, business_date FROM cash_sale_open_shifts WHERE branch_id = $branch;
            """);
        read.Parameters.AddWithValue("$branch", branchId);
        await using var reader = await read.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
    }
}

public sealed record CashSaleResult(string TableSessionId, string EmployeeId, string DeviceId, long ExpectedVersion,
        string ShiftId, string StoreSessionId, string BusinessDate, decimal TenderedAmount, decimal AppliedAmount, decimal ChangeAmount, string ReceiptNumber, string PrintStatus);
