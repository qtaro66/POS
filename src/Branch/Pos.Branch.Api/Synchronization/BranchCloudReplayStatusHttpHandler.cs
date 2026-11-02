using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudReplayStatusHttpHandler
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
            var page = await runtime.CloudReplayStatus.ListAsync(identity.BranchId, 50, after, cancellationToken);
            var s = page.Summary;
            return TypedResults.Ok(new BranchCloudReplayStatusResponse(1, observedAt, new(s.PendingCount, s.LeasedCount, s.UnleasedCount, s.WithRetriesCount),
                page.Items.Select(item => new BranchCloudReplayStatusItemResponse(item.RecoveryId, item.OutboxId, Format(item.ExpectedBranchSequence), Format(item.ReceivedBranchSequence),
                    item.RequestId, item.TableSessionId, item.Action, item.RequestedAt, item.HasLease, item.RetryCount, item.NextRetryAt,
                    item.LeaseExpiresAt, item.LastFailedAt, item.LastExpectedBranchSequence is { } expected ? Format(expected) : null)).ToArray(),
                page.NextAfterSequence is { } next ? Format(next) : null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return TypedResults.Json(new BranchCloudReplayStatusError(1, "CloudReplayStatusUnavailable"), statusCode: 503); }
    }
    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static IResult InvalidQuery() => TypedResults.BadRequest(new BranchCloudReplayStatusError(1, "InvalidCloudReplayStatusQuery"));
}
