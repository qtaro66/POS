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
    public const int MaxAcknowledgmentBodySize = 16 * 1024;

    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _deliveryEndpoint;
    private readonly TimeProvider _timeProvider;

    public HttpBranchCloudOutboxDeliveryClient(
        HttpClient httpClient,
        Uri deliveryEndpoint,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _deliveryEndpoint = RequireDeliveryEndpoint(deliveryEndpoint);
        _timeProvider = timeProvider ?? TimeProvider.System;
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
                var expectedBranchSequence = response.StatusCode is
                        HttpStatusCode.Conflict
                    ? await ReadExpectedBranchSequenceAsync(
                        response,
                        envelope,
                        cancellationToken)
                    : null;
                return BranchCloudOutboxDeliveryResult.Failed(
                    expectedBranchSequence is null
                        ? "HttpStatus" + ((int)response.StatusCode)
                            .ToString(CultureInfo.InvariantCulture)
                        : BranchCloudEventSequenceGapResponse.ErrorCodeValue,
                    ReadRetryAfter(response),
                    expectedBranchSequence);
            }

            if (response.Content.Headers.ContentLength is >
                    MaxAcknowledgmentBodySize)
            {
                return BranchCloudOutboxDeliveryResult.Failed(
                    "AcknowledgmentTooLarge");
            }

            BranchCloudEventIngestionResponse? acknowledgment;
            try
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!IsJsonMediaType(mediaType))
                {
                    return BranchCloudOutboxDeliveryResult.Failed(
                        "InvalidAcknowledgmentPayload");
                }

                var payload = new byte[MaxAcknowledgmentBodySize + 1];
                var length = 0;
                using var stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                while (length < payload.Length)
                {
                    var read = await stream.ReadAsync(
                        payload.AsMemory(length),
                        cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    length += read;
                }

                if (length > MaxAcknowledgmentBodySize)
                {
                    return BranchCloudOutboxDeliveryResult.Failed(
                        "AcknowledgmentTooLarge");
                }

                acknowledgment = JsonSerializer.Deserialize<
                    BranchCloudEventIngestionResponse>(
                    payload.AsSpan(0, length),
                    SerializerOptions);
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
        catch (IOException)
        {
            return BranchCloudOutboxDeliveryResult.Failed("TransportFailure");
        }
    }

    private static bool IsJsonMediaType(string? value) =>
        string.Equals(value, "application/json", StringComparison.OrdinalIgnoreCase) ||
        value?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;

    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        try
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is { } delta)
            {
                return delta > TimeSpan.Zero ? delta : null;
            }

            if (retryAfter?.Date is not { } retryAt)
            {
                return null;
            }

            var delay = retryAt - _timeProvider.GetUtcNow();
            return delay > TimeSpan.Zero ? delay : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static async Task<long?> ReadExpectedBranchSequenceAsync(
        HttpResponseMessage response,
        BranchCloudOutboxEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is >
                MaxAcknowledgmentBodySize ||
            !IsJsonMediaType(response.Content.Headers.ContentType?.MediaType))
        {
            return null;
        }

        try
        {
            var payload = new byte[MaxAcknowledgmentBodySize + 1];
            var length = 0;
            using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            while (length < payload.Length)
            {
                var read = await stream.ReadAsync(
                    payload.AsMemory(length),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > MaxAcknowledgmentBodySize)
            {
                return null;
            }

            var gap = JsonSerializer.Deserialize<
                BranchCloudEventSequenceGapResponse>(
                payload.AsSpan(0, length),
                SerializerOptions);
            return gap is not null &&
                gap.SchemaVersion == CurrentSchemaVersion &&
                string.Equals(
                    gap.ErrorCode,
                    BranchCloudEventSequenceGapResponse.ErrorCodeValue,
                    StringComparison.Ordinal) &&
                string.Equals(
                    gap.BranchId,
                    envelope.BranchId,
                    StringComparison.Ordinal) &&
                gap.ReceivedBranchSequence == envelope.BranchSequence &&
                gap.ExpectedBranchSequence >= 1 &&
                gap.ExpectedBranchSequence < gap.ReceivedBranchSequence
                    ? gap.ExpectedBranchSequence
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
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
