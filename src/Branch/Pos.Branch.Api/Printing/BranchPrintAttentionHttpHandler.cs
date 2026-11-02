using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Printing;
using Pos.Branch.Api.Security;
using Pos.Contracts.Printing;

namespace Pos.Branch.Api.Printing;

public static class BranchPrintAttentionHttpHandler
{
    public const int PageSize = 50;
    private sealed record Cursor(int Version, DateTimeOffset ObservedAt, DateTimeOffset CreatedAt, string PrintJobId);
    private static readonly JsonSerializerOptions CursorJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal, HttpContext context,
        IPrintJobAttentionQuery query, CancellationToken cancellationToken, [FromServices] TimeProvider? clock = null)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (context.Request.ContentLength > 0 || context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        var observedAt = (clock ?? TimeProvider.System).GetUtcNow();
        PrintJobAttentionCursor? after = null;
        try
        {
            if (context.Request.Query.Keys.Any(key => key != "cursor")) { throw new ArgumentException("Unknown query field."); }
            if (context.Request.Query.TryGetValue("cursor", out var values))
            {
                if (values.Count != 1) { throw new ArgumentException("One cursor is required."); }
                var cursor = Decode(values[0]);
                if (cursor.ObservedAt > observedAt) { throw new ArgumentException("Future observation."); }
                observedAt = cursor.ObservedAt;
                after = new PrintJobAttentionCursor(cursor.CreatedAt, cursor.PrintJobId);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or FormatException)
        {
            return TypedResults.BadRequest(new BranchPrintAttentionError(1, "InvalidPrintAttentionQuery"));
        }

        try
        {
            var page = await query.ListAsync(identity.BranchId, observedAt, PageSize, after, cancellationToken);
            var items = page.Items.Select(item => new BranchPrintAttentionItem(
                item.PrintJobId, item.RequestId, item.TableSessionId.Value, item.SnapshotId,
                item.Purpose.ToString(), item.TargetStation.Value, item.Status.ToString(), item.CreatedAt,
                item.AssignedPrinterId, item.NextRetryAt, Reason(item), item.LatestAttemptStatus?.ToString(),
                item.LatestAttemptFinishedAt)).ToArray();
            var next = page.NextCursor is null ? null : Encode(new Cursor(1, observedAt, page.NextCursor.CreatedAt, page.NextCursor.PrintJobId));
            return TypedResults.Ok(new BranchPrintAttentionResponse(1, observedAt, page.TotalCount, items, next));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return TypedResults.Json(new BranchPrintAttentionError(1, "PrintAttentionUnavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string Reason(PrintJobAttentionItem item) => item.Status switch
    {
        PersistentPrintJobStatus.PrintQueued => string.IsNullOrWhiteSpace(item.BlockedReason) ? "AwaitingDispatch" : "DispatchBlocked",
        PersistentPrintJobStatus.Failed => "SendFailed",
        PersistentPrintJobStatus.Unknown => "SendOutcomeUnknown",
        PersistentPrintJobStatus.Dispatching => "DispatchLeaseExpired",
        _ => throw new InvalidOperationException("Unexpected attention state.")
    };

    private static Cursor Decode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Invalid cursor.");
        }
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        var cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(base64), CursorJson);
        if (cursor is null || cursor.Version != 1 || cursor.ObservedAt == default || cursor.CreatedAt == default
            || cursor.ObservedAt.Offset != TimeSpan.Zero || cursor.CreatedAt.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(cursor.PrintJobId) || cursor.PrintJobId.Length > 128
            || cursor.PrintJobId != cursor.PrintJobId.Trim() || cursor.PrintJobId.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid cursor fields.");
        }
        return cursor;
    }

    private static string Encode(Cursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJson))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
