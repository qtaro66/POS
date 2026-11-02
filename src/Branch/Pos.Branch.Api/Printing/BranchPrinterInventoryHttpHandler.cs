using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Printing;
using Pos.Branch.Api.Security;
using Pos.Contracts.Printing;

namespace Pos.Branch.Api.Printing;

public static class BranchPrinterInventoryHttpHandler
{
    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal, HttpContext context, [FromServices] IBranchPrinterInventoryQuery query,
        CancellationToken cancellationToken, [FromServices] TimeProvider? clock = null)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (context.Request.ContentLength > 0 || context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true) { return TypedResults.StatusCode(413); }
        string? after = null;
        if (context.Request.Query.Keys.Any(key => key != "afterPrinterId")) { return InvalidQuery(); }
        if (context.Request.Query.TryGetValue("afterPrinterId", out var values))
        {
            after = values.Count == 1 ? values[0] : null;
            if (string.IsNullOrWhiteSpace(after) || after.Length > 128 || after != after.Trim() || after.Any(char.IsControl)) { return InvalidQuery(); }
        }
        try
        {
            var observedAt = (clock ?? TimeProvider.System).GetUtcNow();
            var page = await query.ListAsync(identity.BranchId, 50, after, cancellationToken);
            return TypedResults.Ok(new BranchPrinterInventoryResponse(1, observedAt, page.TotalCount, page.ActiveCount,
                page.Items.Select(item => new BranchPrinterInventoryItemResponse(item.PrinterId, item.DisplayName, item.ProductionStationId,
                    PublicHost(item.Host), item.Port, item.Protocol, item.CompatibilityProfileId, item.Manufacturer, item.Model, item.ProductCode,
                    item.FirmwareVersion, item.PaperWidthMillimeters, item.StatusMode, item.SupportsCashDrawer, item.IsActive, item.UpdatedAt)).ToArray(), page.NextAfterPrinterId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return TypedResults.Json(new BranchPrinterInventoryError(1, "PrinterInventoryUnavailable"), statusCode: 503); }
    }
    private static string? PublicHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value != value.Trim()
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_' and not ':' and not '%' and not '[' and not ']')) { return null; }
        return IPAddress.TryParse(value, out _) || Uri.CheckHostName(value) == UriHostNameType.Dns ? value : null;
    }
    private static IResult InvalidQuery() => TypedResults.BadRequest(new BranchPrinterInventoryError(1, "InvalidPrinterInventoryQuery"));
}
