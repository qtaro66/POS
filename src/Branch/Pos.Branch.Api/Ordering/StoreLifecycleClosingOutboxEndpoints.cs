using System.Security.Claims;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static partial class StoreLifecycleEndpoints
{
    public static async Task<IResult> ClosingOutboxAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime,
        HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        try
        {
            return TypedResults.Ok(await runtime.TableCommands.ReadClosingReportOutboxAsync(
                identity!.BranchId, runtime.Time.GetUtcNow(), token));
        }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }
}
