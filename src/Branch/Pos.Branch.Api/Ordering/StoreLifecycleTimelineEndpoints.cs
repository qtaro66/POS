using System.Security.Claims;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static partial class StoreLifecycleEndpoints
{
    public static async Task<IResult> WorkspaceAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime,
        HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        var now = runtime.Time.GetUtcNow();
        var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
        try
        {
            return TypedResults.Ok(await runtime.TableCommands.ReadStoreLifecycleWorkspaceAsync(
                identity!.BranchId, businessDate, now, token));
        }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> ShiftClosingAsync(ClaimsPrincipal principal, string storeSessionId, string shiftId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Guid.TryParseExact(storeSessionId, "D", out _) || !Guid.TryParseExact(shiftId, "D", out _))
        {
            return TypedResults.BadRequest();
        }
        try
        {
            var result = await runtime.TableCommands.ReadStoreShiftClosingAsync(identity!.BranchId, storeSessionId, shiftId, token);
            return result is null ? TypedResults.NotFound(new { error = "StoreLifecycleNotFound" }) : TypedResults.Ok(result);
        }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }
}
