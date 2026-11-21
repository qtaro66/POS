using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public static class CentralClosingReportEndpoints
{
    public const string Route = "/api/v1/closing-reports";
    public const int MaximumBodyBytes = 64 * 1024;
    public const string EndpointName = "IngestClosingReport";

    public static IEndpointRouteBuilder MapCentralClosingReportsIfEnabled(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>(CentralClosingReportOptions.SectionName + ":Enabled"))
        {
            endpoints.MapPost(Route, HandleAsync)
                .RequireAuthorization(CentralCloudIngestionAuthorizationDefaults.PolicyName)
                .WithMetadata(new RequestSizeLimitAttribute(MaximumBodyBytes))
                .WithName(EndpointName).ExcludeFromDescription();
        }
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(HttpContext context, [FromServices] IClosingReportIngestionStore store,
        [FromServices] IPostgresClosingReportReadinessProbe readiness, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!AuthenticatedCentralBranchIdentityResolver.TryResolve(context.User, out var identity)) { return TypedResults.Unauthorized(); }
        if (!context.Request.HasJsonContentType()) { return TypedResults.StatusCode(415); }
        if (context.Request.ContentLength > MaximumBodyBytes) { return TypedResults.StatusCode(413); }
        ClosingReportEnvelope report;
        try
        {
            var bytes = new byte[MaximumBodyBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(bytes.AsMemory(length), token);
                if (read == 0) { break; }
                length += read;
            }
            if (length > MaximumBodyBytes) { return TypedResults.StatusCode(413); }
            report = ClosingReportEnvelope.Parse(new UTF8Encoding(false, true).GetString(bytes, 0, length));
        }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidClosingReport" }); }
        catch (JsonException) { return TypedResults.BadRequest(new { error = "InvalidClosingReport" }); }
        catch (OverflowException) { return TypedResults.BadRequest(new { error = "InvalidClosingReport" }); }
        catch (BadHttpRequestException) { return TypedResults.BadRequest(new { error = "InvalidClosingReport" }); }
        if (report.BranchId != identity.BranchId) { return TypedResults.StatusCode(403); }
        try
        {
            if (!await readiness.IsReadyAsync(token)) { return TypedResults.StatusCode(503); }
            var result = await store.PersistAsync(report, token);
            return result switch
            {
                ClosingReportPersistenceResult.Accepted => TypedResults.Ok(new Acknowledgement(1, report.BranchId, report.ReportId, "Accepted")),
                ClosingReportPersistenceResult.AlreadyAccepted => TypedResults.Ok(new Acknowledgement(1, report.BranchId, report.ReportId, "AlreadyAccepted")),
                ClosingReportPersistenceResult.Conflict => TypedResults.Conflict(new { error = "ClosingReportConflict" }),
                _ => TypedResults.StatusCode(503)
            };
        }
        catch (ClosingReportStoreUnavailableException) { return TypedResults.StatusCode(503); }
    }

    public sealed record Acknowledgement(int SchemaVersion, string BranchId, string ReportId, string Status);
}
