using System.Globalization;
using System.Security.Claims;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static partial class CashHistoryEndpoints
{
    public static async Task<IResult> PrintEvidenceAsync(ClaimsPrincipal principal, string requestId, string snapshotId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId) || !ProductionSnapshotIdentifier(snapshotId))
        { return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" }); }
        if (context.Request.Query.Count != 0) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryPrintQuery" }); }
        try
        {
            var evidence = await runtime.TableCommands.ReadCashHistoryPrintEvidenceAsync(identity!.BranchId, requestId,
                snapshotId, runtime.Time.GetUtcNow(), token);
            return evidence is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(evidence);
        }
        catch (KeyNotFoundException) { return TypedResults.NotFound(new { error = "CashHistoryProductionSnapshotNotFound" }); }
        catch (ArgumentException) { return TypedResults.StatusCode(503); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> PrintAttemptsAsync(ClaimsPrincipal principal, string requestId, string snapshotId,
        string printJobId, BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId) || !ProductionSnapshotIdentifier(snapshotId) || !ProductionSnapshotIdentifier(printJobId))
        { return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" }); }
        CashHistoryPrintQuery query;
        try { query = ReadPrintQuery(context.Request.Query); }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryPrintQuery" }); }
        try
        {
            var evidence = await runtime.TableCommands.ReadCashHistoryPrintAttemptsAsync(identity!.BranchId, requestId,
                snapshotId, printJobId, runtime.Time.GetUtcNow(), query, token);
            return evidence is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(evidence);
        }
        catch (CashHistoryPrintJobNotFoundException) { return TypedResults.NotFound(new { error = "CashHistoryPrintJobNotFound" }); }
        catch (KeyNotFoundException) { return TypedResults.NotFound(new { error = "CashHistoryProductionSnapshotNotFound" }); }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryPrintQuery" }); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    private static CashHistoryPrintQuery ReadPrintQuery(IQueryCollection values)
    {
        if (values.Any(pair => pair.Key is not ("limit" or "cursor") || pair.Value.Count != 1 || string.IsNullOrEmpty(pair.Value[0])))
        { throw new ArgumentException("Invalid cash history print query."); }
        var limit = 20;
        var text = values.TryGetValue("limit", out var value) ? value[0] : null;
        var cursor = values.TryGetValue("cursor", out value) ? value[0] : null;
        if ((text is not null && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50)) ||
            (cursor is not null && (cursor.Length > 4096 || cursor != cursor.Trim() || cursor.Any(char.IsControl))))
        { throw new ArgumentException("Invalid cash history print query."); }
        return new(limit, cursor);
    }
}
