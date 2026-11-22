using System.Net;
using System.Text;
using System.Text.Json;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public interface IClosingReportTransport
{
    Task<ClosingReportDeliveryFailure?> SendAsync(ClosingReportLease report, CancellationToken token);
}

public sealed class HttpClosingReportTransport(HttpClient client, Uri endpoint, TimeSpan timeout, TimeProvider time)
    : IClosingReportTransport
{
    public const string Route = "/api/v1/closing-reports";
    private const int MaximumAcknowledgementBytes = 4096;

    public async Task<ClosingReportDeliveryFailure?> SendAsync(ClosingReportLease report, CancellationToken token)
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
            if (response.StatusCode != HttpStatusCode.OK) { return ClosingReportDeliveryFailure.CentralRejected; }
            if (response.Content.Headers.ContentLength > MaximumAcknowledgementBytes)
            {
                return ClosingReportDeliveryFailure.AcknowledgmentUnavailable;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var bytes = new byte[MaximumAcknowledgementBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(length), linked.Token);
                if (read == 0) { break; }
                length += read;
            }
            if (length > MaximumAcknowledgementBytes) { return ClosingReportDeliveryFailure.AcknowledgmentUnavailable; }
            return ReadAcknowledgement(bytes.AsMemory(0, length), report);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return ClosingReportDeliveryFailure.TransportUnavailable; }
        catch (HttpRequestException) { return ClosingReportDeliveryFailure.TransportUnavailable; }
        catch (IOException) { return ClosingReportDeliveryFailure.TransportUnavailable; }
        catch (JsonException) { return ClosingReportDeliveryFailure.AcknowledgmentUnavailable; }
    }

    private static ClosingReportDeliveryFailure? ReadAcknowledgement(ReadOnlyMemory<byte> bytes, ClosingReportLease report)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count() ||
                !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
                !Matches(root, "branchId", report.BranchId) || !Matches(root, "reportId", report.ReportId) ||
                !(Matches(root, "status", "Accepted") || Matches(root, "status", "AlreadyAccepted")))
            {
                return ClosingReportDeliveryFailure.AcknowledgmentUnavailable;
            }
            return null;
        }
        catch (JsonException) { return ClosingReportDeliveryFailure.AcknowledgmentUnavailable; }
        catch (InvalidOperationException) { return ClosingReportDeliveryFailure.AcknowledgmentUnavailable; }
    }

    private static bool Matches(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.Ordinal);
}
