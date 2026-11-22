using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public static class CentralClosingReportContextEndpoints
{
    public const string Route = "/api/v1/closing-reports/lifecycle";
    public const string EndpointName = "IngestClosingReportLifecycle";
    public const int MaximumBodyBytes = 1024 * 1024;
    public static IEndpointRouteBuilder MapCentralClosingReportContextsIfEnabled(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>(CentralClosingReportOptions.SectionName + ":Enabled"))
        {
            endpoints.MapPost(Route, HandleAsync).RequireAuthorization(CentralCloudIngestionAuthorizationDefaults.PolicyName)
                .WithMetadata(new RequestSizeLimitAttribute(MaximumBodyBytes)).WithName(EndpointName).ExcludeFromDescription();
        }
        return endpoints;
    }
    public static async Task<IResult> HandleAsync(HttpContext context, [FromServices] IClosingReportContextIngestionStore store,
        [FromServices] IPostgresClosingReportContextReadinessProbe readiness, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!AuthenticatedCentralBranchIdentityResolver.TryResolve(context.User, out var identity)) { return TypedResults.Unauthorized(); }
        if (!context.Request.HasJsonContentType()) { return TypedResults.StatusCode(415); }
        if (context.Request.ContentLength > MaximumBodyBytes) { return TypedResults.StatusCode(413); }
        ClosingReportContextEnvelope report;
        try
        {
            var bytes = new byte[MaximumBodyBytes + 1]; var length = 0;
            while (length < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(bytes.AsMemory(length), token);
                if (read == 0) { break; }
                length += read;
            }
            if (length > MaximumBodyBytes) { return TypedResults.StatusCode(413); }
            report = ClosingReportContextEnvelope.Parse(new UTF8Encoding(false, true).GetString(bytes, 0, length));
        }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidClosingReportContext" }); }
        catch (JsonException) { return TypedResults.BadRequest(new { error = "InvalidClosingReportContext" }); }
        catch (OverflowException) { return TypedResults.BadRequest(new { error = "InvalidClosingReportContext" }); }
        catch (BadHttpRequestException) { return TypedResults.BadRequest(new { error = "InvalidClosingReportContext" }); }
        if (report.BranchId != identity.BranchId) { return TypedResults.StatusCode(403); }
        try
        {
            if (!await readiness.IsReadyAsync(token)) { return TypedResults.StatusCode(503); }
            return await store.PersistAsync(report, token) switch
            {
                ClosingReportContextPersistenceResult.Accepted => TypedResults.Ok(new Acknowledgement(1, report.ContextId, report.ReportId, report.BranchId, "Accepted")),
                ClosingReportContextPersistenceResult.AlreadyAccepted => TypedResults.Ok(new Acknowledgement(1, report.ContextId, report.ReportId, report.BranchId, "AlreadyAccepted")),
                ClosingReportContextPersistenceResult.ParentMissing => TypedResults.Conflict(new { error = "ClosingReportParentMissing" }),
                ClosingReportContextPersistenceResult.Conflict => TypedResults.Conflict(new { error = "ClosingReportContextConflict" }),
                _ => TypedResults.StatusCode(503)
            };
        }
        catch (ClosingReportStoreUnavailableException) { return TypedResults.StatusCode(503); }
    }
    public sealed record Acknowledgement(int SchemaVersion, string ContextId, string ReportId, string BranchId, string Status);
}
