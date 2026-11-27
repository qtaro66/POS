using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public static class KitchenReprintEndpoints
{
    [JsonConverter(typeof(RequestConverter))]
    public sealed record Request(int SchemaVersion, string CommandRequestId, string OriginalPrintJobId, string Reason, string Pin)
    {
        public override string ToString() => nameof(Request);
    }

    public sealed class RequestConverter : JsonConverter<Request>
    {
        public override Request Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var root = document.RootElement;
                var allowed = new HashSet<string>(["schemaVersion", "commandRequestId", "originalPrintJobId", "reason", "pin"], StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property =>
                    !allowed.Contains(property.Name) || !seen.Add(property.Name)) || seen.Count != allowed.Count)
                { throw new JsonException("Invalid kitchen reprint request."); }
                string Text(string name) => root.GetProperty(name).ValueKind == JsonValueKind.String
                    ? root.GetProperty(name).GetString()! : throw new JsonException("Invalid kitchen reprint request.");
                return new(root.GetProperty("schemaVersion").GetInt32(), Text("commandRequestId"), Text("originalPrintJobId"), Text("reason"), Text("pin"));
            }
            catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            { throw new JsonException("Invalid kitchen reprint request."); }
        }

        public override void Write(Utf8JsonWriter writer, Request value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", value.SchemaVersion);
            writer.WriteString("commandRequestId", value.CommandRequestId);
            writer.WriteString("originalPrintJobId", value.OriginalPrintJobId);
            writer.WriteString("reason", value.Reason);
            writer.WriteString("pin", value.Pin);
            writer.WriteEndObject();
        }
    }

    public static async Task<IResult> QueueAsync(ClaimsPrincipal principal, string requestId, string snapshotId,
        string orderLineId, Request? request, BranchOrderingRuntime runtime, [FromServices] BranchEmployeePinAuthenticationService pins,
        HttpContext context, CancellationToken token, [FromServices] BranchEmployeePermissionCache? permissions = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out _)) { return TypedResults.StatusCode(403); }
        if (context.Request.Query.Count != 0 || request is null || request.SchemaVersion != 1 || !Guid.TryParseExact(request.CommandRequestId, "D", out var commandId) ||
            !Reason(request.Reason)) { return TypedResults.BadRequest(new { error = "InvalidKitchenReprintRequest" }); }
        var canonicalCommandId = commandId.ToString("D");
        var target = new KitchenReprintAuthorizationTarget(canonicalCommandId, requestId, snapshotId, orderLineId, request.OriginalPrintJobId);
        var decision = await new KitchenReprintAuthorizationService(pins, permissions).AuthorizeAsync(principal,
            request.Pin.AsMemory(), target, token);
        if (decision.Outcome != KitchenReprintAuthorizationOutcome.Authorized || decision.Operator is null)
        {
            return decision.Outcome switch
            {
                KitchenReprintAuthorizationOutcome.Forbidden => TypedResults.StatusCode(403),
                KitchenReprintAuthorizationOutcome.PinRejected => TypedResults.Unauthorized(),
                KitchenReprintAuthorizationOutcome.Unavailable => TypedResults.StatusCode(503),
                _ => TypedResults.BadRequest(new { error = "InvalidKitchenReprintRequest" })
            };
        }
        var actor = decision.Operator;
        try
        {
            var receipt = await runtime.TableCommands.WriteKitchenReprintAsync(actor.Requester.BranchId,
                new(canonicalCommandId, requestId, snapshotId, orderLineId, request.OriginalPrintJobId,
                    actor.Requester.DeviceId, actor.Requester.EmployeeId, actor.ApproverEmployeeId, request.Reason, runtime.Time.GetUtcNow()), token);
            return receipt is null ? TypedResults.NotFound(new { error = "CashHistoryNotFound" }) : TypedResults.Ok(receipt);
        }
        catch (KitchenReprintLineNotFoundException) { return TypedResults.NotFound(new { error = "KitchenReprintLineNotFound" }); }
        catch (CashHistoryPrintJobNotFoundException) { return TypedResults.NotFound(new { error = "CashHistoryPrintJobNotFound" }); }
        catch (KeyNotFoundException) { return TypedResults.NotFound(new { error = "CashHistoryProductionSnapshotNotFound" }); }
        catch (KitchenReprintConflictException) { return TypedResults.Conflict(new { error = "KitchenReprintConflict" }); }
        catch (ArgumentException) { return TypedResults.BadRequest(new { error = "InvalidKitchenReprintRequest" }); }
        catch (Exception error) when (Unavailable(error)) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> StatusAsync(ClaimsPrincipal principal, string commandRequestId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Guid.TryParseExact(commandRequestId, "D", out var commandId) || context.Request.Query.Count != 0)
        { return TypedResults.BadRequest(new { error = "InvalidKitchenReprintRequest" }); }
        try
        {
            var status = await runtime.TableCommands.ReadKitchenReprintStatusAsync(identity!.BranchId, identity.EmployeeId,
                identity.DeviceId, commandId.ToString("D"), runtime.Time.GetUtcNow(), token);
            return status is null ? TypedResults.NotFound(new { error = "KitchenReprintNotFound" }) : TypedResults.Ok(status);
        }
        catch (Exception error) when (Unavailable(error) || error is ArgumentException) { return TypedResults.StatusCode(503); }
    }

    private static bool TryIdentity(ClaimsPrincipal principal, out AuthenticatedBranchRequestIdentity? identity) =>
        AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out identity) &&
        AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) && role == "PcPos";
    private static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 &&
        !value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    private static bool Unavailable(Exception error) => error is SqliteException or JsonException or InvalidDataException or
        InvalidOperationException or IOException or FormatException or OverflowException;
}
