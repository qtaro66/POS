using System.Security.Claims;

namespace Pos.Branch.Api.Ordering;

public static partial class CashHistoryEndpoints
{
    public static async Task<IResult> ShiftContextAsync(ClaimsPrincipal principal, string requestId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId)) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" }); }
        if (context.Request.Query.Count != 0) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryContextQuery" }); }
        try
        {
            var evidence = await runtime.TableCommands.ReadCashHistoryShiftContextAsync(identity!.BranchId,
                requestId, runtime.Time.GetUtcNow(), token);
            return evidence is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(evidence);
        }
        catch (ArgumentException) { return TypedResults.StatusCode(503); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> ReceiptEvidenceAsync(ClaimsPrincipal principal, string requestId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Identifier(requestId)) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryIdentity" }); }
        if (context.Request.Query.Count != 0) { return TypedResults.BadRequest(new { error = "InvalidCashHistoryContextQuery" }); }
        try
        {
            var evidence = await runtime.TableCommands.ReadCashHistoryReceiptEvidenceAsync(identity!.BranchId,
                requestId, runtime.Time.GetUtcNow(), token);
            return evidence is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(evidence);
        }
        catch (ArgumentException) { return TypedResults.StatusCode(503); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }
}
