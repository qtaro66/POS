using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

internal sealed record BranchReadinessResponse(int SchemaVersion, string CheckedAt, bool OrderingDatabaseReady);

internal static class BranchReadinessHttpHandler
{
    public static IResult Handle(HttpContext context, IOptions<BranchOrderingOptions> options)
    {
        context.Response.Headers.CacheControl = "no-store";
        var ready = SqliteBranchOrderingReadinessProbe.IsReady(options.Value.DatabasePath);
        return Results.Ok(new BranchReadinessResponse(1, DateTimeOffset.UtcNow.UtcDateTime.ToString("O"), ready));
    }
}
