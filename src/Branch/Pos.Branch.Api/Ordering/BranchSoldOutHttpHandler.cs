using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

internal sealed record BranchSoldOutResponse(int SchemaVersion, long TotalCount, int? NextOffset, IReadOnlyList<BranchSoldOutRow> Items);

internal static class BranchSoldOutHttpHandler
{
    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal, HttpContext context, BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (context.Request.ContentLength > 0 || context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true) { return TypedResults.StatusCode(413); }
        var query = context.Request.Query;
        if (query.Any(entry => entry.Key is not ("search" or "offset") || entry.Value.Count != 1)) { return TypedResults.BadRequest(); }
        var search = query["search"].ToString().Trim();
        var offset = 0;
        if (search.Length > 100 || search.Any(char.IsControl) ||
            (query.TryGetValue("offset", out var raw) &&
            (!int.TryParse(raw.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out offset)
            || offset < 0 || offset > int.MaxValue - SqliteBranchSoldOutQuery.PageSize)))
        {
            return TypedResults.BadRequest();
        }
        try
        {
            var (total, items) = await runtime.SoldOut.ReadAsync(identity.BranchId, search, offset, cancellationToken);
            var next = offset + items.Count;
            return TypedResults.Ok(new BranchSoldOutResponse(1, total, items.Count > 0 && next < total ? next : null, items));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException or FormatException)
        {
            return TypedResults.StatusCode(503);
        }
    }
}
