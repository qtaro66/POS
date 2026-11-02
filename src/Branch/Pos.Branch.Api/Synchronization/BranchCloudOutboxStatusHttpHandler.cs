using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudOutboxStatusHttpHandler
{
    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal, HttpContext context, BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (context.Request.ContentLength > 0 || context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true) { return TypedResults.StatusCode(413); }
        long? after = null;
        if (context.Request.Query.Keys.Any(key => key != "afterSequence")) { return InvalidQuery(); }
        if (context.Request.Query.TryGetValue("afterSequence", out var values))
        {
            var value = values.Count == 1 ? values[0] : null;
            if (value is null || value.Length is < 1 or > 19 || value.Any(character => !char.IsAsciiDigit(character))
                || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || Format(parsed) != value) { return InvalidQuery(); }
            after = parsed;
        }
        try
        {
            var observedAt = runtime.Time.GetUtcNow();
            var page = await runtime.CloudOutboxStatus.ListAsync(identity.BranchId, 50, after, cancellationToken);
            var summary = page.Summary;
            return TypedResults.Ok(new BranchCloudOutboxStatusResponse(1, observedAt,
                new(summary.UnacknowledgedCount, summary.PendingCount, summary.ProcessingCount, summary.FailedCount, summary.OtherCount),
                page.Items.Select(item => new BranchCloudOutboxStatusItemResponse(item.OutboxId, Format(item.BranchSequence), item.RequestId,
                    item.TableSessionId, item.Action, item.ProcessingStatus, item.CreatedAt, item.RetryCount, item.NextRetryAt,
                    item.LeaseExpiresAt, item.LastFailedAt, item.ExpectedBranchSequence is { } expected ? Format(expected) : null)).ToArray(),
                page.NextAfterSequence is { } next ? Format(next) : null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return TypedResults.Json(new BranchCloudOutboxStatusError(1, "CloudOutboxStatusUnavailable"), statusCode: 503); }
    }
    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static IResult InvalidQuery() => TypedResults.BadRequest(new BranchCloudOutboxStatusError(1, "InvalidCloudOutboxStatusQuery"));
}
