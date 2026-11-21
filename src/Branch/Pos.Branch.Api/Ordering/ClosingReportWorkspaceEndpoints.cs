using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static partial class StoreLifecycleEndpoints
{
    public sealed record ClosingReportDeliveryStatus(string BranchId, DateTimeOffset AsOf, bool DeliveryEnabled);

    public static async Task<IResult> ClosingReportsAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime,
        HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        try
        {
            var query = ReadClosingReportQuery(context.Request.Query);
            return TypedResults.Ok(await runtime.TableCommands.ReadClosingReportPageAsync(identity!.BranchId,
                runtime.Time.GetUtcNow(), query, token));
        }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidClosingReportQuery" }); }
        catch (JsonException) { return TypedResults.StatusCode(503); }
        catch (InvalidDataException) { return TypedResults.StatusCode(503); }
        catch (InvalidOperationException) { return TypedResults.StatusCode(503); }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> ClosingReportDetailAsync(ClaimsPrincipal principal, string requestId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Guid.TryParseExact(requestId, "D", out _)) { return TypedResults.BadRequest(new { error = "InvalidClosingReportIdentity" }); }
        try
        {
            var report = await runtime.TableCommands.ReadClosingReportDetailAsync(identity!.BranchId, requestId,
                runtime.Time.GetUtcNow(), token);
            return report is null ? TypedResults.NotFound(new { error = "ClosingReportNotFound" }) : TypedResults.Ok(report);
        }
        catch (ArgumentException) { return TypedResults.StatusCode(503); }
        catch (JsonException) { return TypedResults.StatusCode(503); }
        catch (InvalidDataException) { return TypedResults.StatusCode(503); }
        catch (InvalidOperationException) { return TypedResults.StatusCode(503); }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    public static IResult ClosingReportDeliveryAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime,
        HttpContext context, [FromServices] IOptions<ClosingReportDeliveryOptions>? delivery = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        var options = delivery?.Value;
        return TypedResults.Ok(new ClosingReportDeliveryStatus(identity!.BranchId, runtime.Time.GetUtcNow(),
            options?.Enabled == true && string.Equals(options.BranchId, identity.BranchId, StringComparison.Ordinal)));
    }

    private static ClosingReportQuery ReadClosingReportQuery(IQueryCollection values)
    {
        var allowed = new HashSet<string>(["status", "storeSessionId", "shiftId", "businessDate", "limit", "cursor"], StringComparer.Ordinal);
        if (values.Any(pair => !allowed.Contains(pair.Key) || pair.Value.Count != 1 || string.IsNullOrEmpty(pair.Value[0])))
        {
            throw new ArgumentException("Invalid closing report query.");
        }
        string? Optional(string key) => values.TryGetValue(key, out var value) ? value[0] : null;
        var status = Optional("status") ?? "All";
        var store = Optional("storeSessionId");
        var shift = Optional("shiftId");
        var date = Optional("businessDate");
        var limit = 20;
        if (status is not ("All" or "Pending" or "Delivered") ||
            (store is not null && !Guid.TryParseExact(store, "D", out _)) ||
            (shift is not null && !Guid.TryParseExact(shift, "D", out _)) ||
            (date is not null && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) ||
            (Optional("limit") is { } text && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50)))
        {
            throw new ArgumentException("Invalid closing report query.");
        }
        return new(status, store, shift, date, limit, Optional("cursor"));
    }
}
