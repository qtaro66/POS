using System.Globalization;
using System.Security.Claims;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static partial class CashHistoryEndpoints
{
    public static async Task<IResult> ProductionOrdersAsync(ClaimsPrincipal principal, string requestId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId)) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" }); }
        CashHistoryProductionQuery query;
        try { query = ReadProductionQuery(context.Request.Query); }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryProductionQuery" }); }
        try
        {
            var page = await runtime.TableCommands.ReadCashHistoryProductionOrdersAsync(identity!.BranchId, requestId,
                runtime.Time.GetUtcNow(), query, token);
            return page is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(page);
        }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryProductionQuery" }); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> ProductionOrderDetailAsync(ClaimsPrincipal principal, string requestId, string snapshotId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId) || !ProductionSnapshotIdentifier(snapshotId))
        {
            return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" });
        }
        if (context.Request.Query.Count != 0) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryProductionQuery" }); }
        try
        {
            var detail = await runtime.TableCommands.ReadCashHistoryProductionOrderDetailAsync(identity!.BranchId, requestId,
                snapshotId, runtime.Time.GetUtcNow(), token);
            return detail is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(detail);
        }
        catch (KeyNotFoundException) { return TypedResults.NotFound(new { error = "CashHistoryProductionSnapshotNotFound" }); }
        catch (ArgumentException) { return TypedResults.StatusCode(503); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    private static bool ProductionSnapshotIdentifier(string value) => !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() && value.Length <= 1024 && !value.Any(char.IsControl);

    private static CashHistoryProductionQuery ReadProductionQuery(IQueryCollection values)
    {
        var allowed = new HashSet<string>(["orderBatchId", "orderLineId", "productionStationId", "limit", "cursor"], StringComparer.Ordinal);
        if (values.Any(pair => !allowed.Contains(pair.Key) || pair.Value.Count != 1 || string.IsNullOrEmpty(pair.Value[0])))
        {
            throw new ArgumentException("Invalid cash history production query.");
        }
        string? Optional(string key) => values.TryGetValue(key, out var value) ? value[0] : null;
        var batch = Optional("orderBatchId");
        var line = Optional("orderLineId");
        var station = Optional("productionStationId");
        var cursor = Optional("cursor");
        var limit = 20;
        if ((batch is not null && !Identifier(batch)) || (line is not null && !Identifier(line)) ||
            (station is not null && !Identifier(station)) ||
            (cursor is not null && (cursor.Length > 4096 || cursor != cursor.Trim() || cursor.Any(char.IsControl))) ||
            (Optional("limit") is { } text && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50)))
        {
            throw new ArgumentException("Invalid cash history production query.");
        }
        return new(batch, line, station, limit, cursor);
    }
}
