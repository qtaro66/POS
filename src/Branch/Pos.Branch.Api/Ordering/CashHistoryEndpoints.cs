using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static class CashHistoryEndpoints
{
    public static async Task<IResult> EventsAsync(ClaimsPrincipal principal, string requestId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId)) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" }); }
        CashHistoryEventQuery query;
        try { query = ReadEventQuery(context.Request.Query); }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryEventQuery" }); }
        try
        {
            var page = await runtime.TableCommands.ReadCashHistoryEventsAsync(identity!.BranchId, requestId,
                runtime.Time.GetUtcNow(), query, token);
            return page is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(page);
        }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryEventQuery" }); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> ListAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime,
        HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        CashHistoryQuery query;
        try { query = ReadQuery(context.Request.Query); }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryQuery" }); }
        try
        {
            return TypedResults.Ok(await runtime.TableCommands.ReadCashHistoryPageAsync(identity!.BranchId,
                runtime.Time.GetUtcNow(), query, token));
        }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryQuery" }); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> DetailAsync(ClaimsPrincipal principal, string requestId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId)) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" }); }
        try
        {
            var detail = await runtime.TableCommands.ReadCashHistoryDetailAsync(identity!.BranchId, requestId,
                runtime.Time.GetUtcNow(), token);
            return detail is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(detail);
        }
        catch (ArgumentException) { return TypedResults.StatusCode(503); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    private static bool Unavailable(Exception error) => error is JsonException or InvalidDataException or
        InvalidOperationException or SqliteException or OverflowException or FormatException;

    private static bool Identifier(string value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
        value.Length <= 256 && !value.Any(char.IsControl);

    private static bool TryIdentity(ClaimsPrincipal principal, out AuthenticatedBranchRequestIdentity? identity) =>
        AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out identity) &&
        AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) && role == "PcPos";

    private static CashHistoryQuery ReadQuery(IQueryCollection values)
    {
        var allowed = new HashSet<string>(["businessDate", "receiptNumber", "tableSessionId", "paymentMethod", "status", "limit", "cursor", "tableId", "orderBatchId"], StringComparer.Ordinal);
        if (values.Any(pair => !allowed.Contains(pair.Key) || pair.Value.Count != 1 || string.IsNullOrEmpty(pair.Value[0])))
        {
            throw new ArgumentException("Invalid cash history query.");
        }
        string? Optional(string key) => values.TryGetValue(key, out var value) ? value[0] : null;
        var date = Optional("businessDate");
        var receipt = Optional("receiptNumber");
        var session = Optional("tableSessionId");
        var table = Optional("tableId");
        var batch = Optional("orderBatchId");
        var method = Optional("paymentMethod") ?? "All";
        var status = Optional("status") ?? "All";
        var limit = 20;
        if ((date is not null && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) ||
            (receipt is not null && (string.IsNullOrWhiteSpace(receipt) || receipt != receipt.Trim() || receipt.Length > 128 || receipt.Any(char.IsControl))) ||
            (session is not null && !Identifier(session)) || (table is not null && !Identifier(table)) ||
            (batch is not null && !Identifier(batch)) || method is not ("All" or "Cash") ||
            status is not ("All" or "Completed") ||
            (Optional("limit") is { } text && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50)))
        {
            throw new ArgumentException("Invalid cash history query.");
        }
        return new(date, receipt, session, method, status, limit, Optional("cursor"), table, batch);
    }

    private static CashHistoryEventQuery ReadEventQuery(IQueryCollection values)
    {
        var allowed = new HashSet<string>(["source", "limit", "cursor"], StringComparer.Ordinal);
        if (values.Any(pair => !allowed.Contains(pair.Key) || pair.Value.Count != 1 || string.IsNullOrEmpty(pair.Value[0])))
        {
            throw new ArgumentException("Invalid cash history event query.");
        }
        string? Optional(string key) => values.TryGetValue(key, out var value) ? value[0] : null;
        var source = Optional("source") ?? "Session";
        var limit = 20;
        if (source is not ("Session" or "Cancellation") ||
            (Optional("limit") is { } text && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50)))
        {
            throw new ArgumentException("Invalid cash history event query.");
        }
        return new(source, limit, Optional("cursor"));
    }
}
