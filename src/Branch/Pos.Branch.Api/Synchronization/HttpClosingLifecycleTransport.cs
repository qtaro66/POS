using System.Net;
using System.Text;
using System.Text.Json;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public interface IClosingLifecycleTransport
{
    Task<ClosingLifecycleFailure?> SendAsync(ClosingLifecycleLease report, CancellationToken token);
}

public sealed class HttpClosingLifecycleTransport(HttpClient client, Uri endpoint, TimeSpan timeout, TimeProvider time)
    : IClosingLifecycleTransport
{
    public const string Route = "/api/v1/closing-reports/lifecycle";
    public const int MaximumAcknowledgementBytes = 8192;

    public async Task<ClosingLifecycleFailure?> SendAsync(ClosingLifecycleLease report, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(report.PayloadJson, Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (response.Content.Headers.ContentLength > MaximumAcknowledgementBytes) { return ClosingLifecycleFailure.AcknowledgmentUnavailable; }
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var bytes = new byte[MaximumAcknowledgementBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(length), linked.Token);
                if (read == 0) { break; }
                length += read;
            }
            if (length > MaximumAcknowledgementBytes) { return ClosingLifecycleFailure.AcknowledgmentUnavailable; }
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict)) { return ClosingLifecycleFailure.CentralRejected; }
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
            {
                return ClosingLifecycleFailure.AcknowledgmentUnavailable;
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return Matches(root, "error", "ClosingReportParentMissing")
                    ? ClosingLifecycleFailure.ParentUnavailable : ClosingLifecycleFailure.CentralRejected;
            }
            if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != 1 || !Matches(root, "branchId", report.BranchId) ||
                !Matches(root, "contextId", report.ContextId) || !Matches(root, "reportId", report.ReportId) ||
                !(Matches(root, "status", "Accepted") || Matches(root, "status", "AlreadyAccepted")))
            {
                return ClosingLifecycleFailure.AcknowledgmentUnavailable;
            }
            return null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return ClosingLifecycleFailure.TransportUnavailable; }
        catch (HttpRequestException) { return ClosingLifecycleFailure.TransportUnavailable; }
        catch (IOException) { return ClosingLifecycleFailure.TransportUnavailable; }
        catch (JsonException) { return ClosingLifecycleFailure.AcknowledgmentUnavailable; }
    }
    private static bool Matches(JsonElement root, string name, string value) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && property.GetString() == value;
}
