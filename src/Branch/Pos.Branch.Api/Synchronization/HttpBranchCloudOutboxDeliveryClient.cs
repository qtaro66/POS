using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public sealed class HttpBranchCloudOutboxDeliveryClient :
    IBranchCloudOutboxDeliveryClient
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _deliveryEndpoint;

    public HttpBranchCloudOutboxDeliveryClient(
        HttpClient httpClient,
        Uri deliveryEndpoint)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _deliveryEndpoint = RequireDeliveryEndpoint(deliveryEndpoint);
    }

    public async Task<BranchCloudOutboxDeliveryResult> DeliverAsync(
        BranchCloudOutboxEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var request = new BranchCloudEventIngestionRequest(
            CurrentSchemaVersion,
            envelope.EventId,
            envelope.BranchId,
            envelope.BranchSequence,
            envelope.RequestId,
            envelope.TableSessionId.Value,
            envelope.Action.ToString(),
            envelope.TableVersion,
            envelope.OccurredAt);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _deliveryEndpoint)
            {
                Content = JsonContent.Create(
                    request,
                    options: SerializerOptions)
            };
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode is not HttpStatusCode.OK)
            {
                return BranchCloudOutboxDeliveryResult.Failed(
                    "HttpStatus" + ((int)response.StatusCode)
                        .ToString(CultureInfo.InvariantCulture));
            }

            BranchCloudEventIngestionResponse? acknowledgment;
            try
            {
                acknowledgment = await response.Content.ReadFromJsonAsync<
                    BranchCloudEventIngestionResponse>(
                    SerializerOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return BranchCloudOutboxDeliveryResult.Failed(
                    "InvalidAcknowledgmentPayload");
            }
            catch (NotSupportedException)
            {
                return BranchCloudOutboxDeliveryResult.Failed(
                    "InvalidAcknowledgmentPayload");
            }

            if (acknowledgment is null ||
                acknowledgment.SchemaVersion != CurrentSchemaVersion ||
                !string.Equals(
                    acknowledgment.EventId,
                    envelope.EventId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    acknowledgment.BranchId,
                    envelope.BranchId,
                    StringComparison.Ordinal) ||
                acknowledgment.BranchSequence != envelope.BranchSequence)
            {
                return BranchCloudOutboxDeliveryResult.Failed(
                    "AcknowledgmentIdentityMismatch");
            }

            return BranchCloudOutboxDeliveryResult.Acknowledged(
                acknowledgment.EventId,
                acknowledgment.BranchSequence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BranchCloudOutboxDeliveryResult.Failed("RequestTimeout");
        }
        catch (HttpRequestException)
        {
            return BranchCloudOutboxDeliveryResult.Failed("TransportFailure");
        }
    }

    private static Uri RequireDeliveryEndpoint(Uri? value)
    {
        if (value?.IsAbsoluteUri != true ||
            !string.Equals(
                value.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(value.Host) ||
            !string.IsNullOrEmpty(value.UserInfo) ||
            !string.IsNullOrEmpty(value.Query) ||
            !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException(
                "The Cloud delivery endpoint must be an absolute HTTPS URI without credentials, query, or fragment.",
                nameof(value));
        }

        return value;
    }
}
