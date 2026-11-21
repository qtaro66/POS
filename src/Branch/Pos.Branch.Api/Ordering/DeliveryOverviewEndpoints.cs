using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static partial class StoreLifecycleEndpoints
{
    public sealed record DeliveryOverviewResponse(int SchemaVersion, BranchDeliverySnapshot Snapshot,
        bool TableDeliveryEnabled, bool ReportDeliveryEnabled);

    public static async Task<IResult> DeliveryOverviewAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime,
        HttpContext context, CancellationToken token,
        [FromServices] IOptions<BranchCloudOutboxDeliveryOptions>? tableDelivery = null,
        [FromServices] IOptions<ClosingReportDeliveryOptions>? reportDelivery = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (context.Request.Query.Count != 0) { return TypedResults.BadRequest(new { error = "InvalidDeliveryOverviewQuery" }); }
        try
        {
            var snapshot = await runtime.DeliveryOverview.ReadAsync(identity!.BranchId, runtime.Time.GetUtcNow(), token);
            var table = tableDelivery?.Value;
            var reports = reportDelivery?.Value;
            var tableEnabled = table?.Enabled == true && table.BranchId == identity.BranchId &&
                DeliveryDatabaseMatches(table.DatabasePath, runtime.DatabasePath);
            var reportsEnabled = reports?.Enabled == true && reports.BranchId == identity.BranchId;
            return TypedResults.Ok(new DeliveryOverviewResponse(1, snapshot, tableEnabled, reportsEnabled));
        }
        catch (InvalidDataException) { return TypedResults.StatusCode(503); }
        catch (FormatException) { return TypedResults.StatusCode(503); }
        catch (InvalidOperationException) { return TypedResults.StatusCode(503); }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    private static bool DeliveryDatabaseMatches(string candidate, string orderingPath)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate)) { return false; }
        try
        {
            return string.Equals(Path.GetFullPath(candidate), orderingPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }
}
