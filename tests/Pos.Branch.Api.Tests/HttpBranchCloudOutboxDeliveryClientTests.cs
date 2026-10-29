using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Tests;

public sealed class HttpBranchCloudOutboxDeliveryClientTests
{
    private static readonly Uri Endpoint =
        new("https://cloud.example.test/api/v1/branch-events");

    [Theory]
    [InlineData("http://cloud.example.test/api/v1/branch-events")]
    [InlineData("https://user@cloud.example.test/api/v1/branch-events")]
    [InlineData("https://cloud.example.test/api/v1/branch-events?mode=test")]
    public void Insecure_or_ambiguous_endpoint_is_rejected(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            new HttpBranchCloudOutboxDeliveryClient(
                new HttpClient(new StubHandler((_, _) =>
                    throw new InvalidOperationException())),
                new Uri(endpoint)));
    }

    [Fact]
    public async Task Matching_durable_acknowledgment_is_returned()
    {
        BranchCloudEventIngestionRequest? sent = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Endpoint, request.RequestUri);
            sent = await request.Content!.ReadFromJsonAsync<
                BranchCloudEventIngestionRequest>(cancellationToken);
            return JsonResponse(
                new BranchCloudEventIngestionResponse(
                    1,
                    "OUTBOX-01",
                    "BRANCH-01",
                    7));
        });
        var client = Client(handler);

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
            result.Disposition);
        Assert.Equal("OUTBOX-01", result.AcknowledgedEventId);
        Assert.Equal(7, result.AcknowledgedBranchSequence);
        Assert.NotNull(sent);
        Assert.Equal(1, sent.SchemaVersion);
        Assert.Equal("REQUEST-01", sent.RequestId);
        Assert.Equal("SESSION-01", sent.TableSessionId);
        Assert.Equal("SubmitOrder", sent.Action);
        Assert.Equal(3, sent.TableVersion);
    }

    [Fact]
    public async Task Service_unavailable_is_retryable()
    {
        var client = Client(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(
                HttpStatusCode.ServiceUnavailable))));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("HttpStatus503", result.ErrorCode);
    }

    [Fact]
    public async Task Mismatched_acknowledgment_is_retryable()
    {
        var client = Client(new StubHandler((_, _) => Task.FromResult(
            JsonResponse(
                new BranchCloudEventIngestionResponse(
                    1,
                    "OTHER-EVENT",
                    "BRANCH-01",
                    7)))));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("AcknowledgmentIdentityMismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Invalid_acknowledgment_payload_is_retryable()
    {
        var client = Client(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{not-json",
                    Encoding.UTF8,
                    "application/json")
            })));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("InvalidAcknowledgmentPayload", result.ErrorCode);
    }

    [Fact]
    public async Task Non_json_acknowledgment_is_retryable()
    {
        var client = Client(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("acknowledged")
            })));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("InvalidAcknowledgmentPayload", result.ErrorCode);
    }

    [Fact]
    public async Task Declared_oversized_acknowledgment_is_retryable()
    {
        var content = new ByteArrayContent(
            new byte[
                HttpBranchCloudOutboxDeliveryClient
                    .MaxAcknowledgmentBodySize + 1]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var client = Client(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            })));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("AcknowledgmentTooLarge", result.ErrorCode);
    }

    [Fact]
    public async Task Chunked_oversized_acknowledgment_is_retryable()
    {
        var content = new UnknownLengthContent(
            new byte[
                HttpBranchCloudOutboxDeliveryClient
                    .MaxAcknowledgmentBodySize + 1]);
        var client = Client(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            })));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("AcknowledgmentTooLarge", result.ErrorCode);
    }

    [Fact]
    public async Task Transport_failure_is_retryable()
    {
        var client = Client(new StubHandler((_, _) =>
            throw new HttpRequestException("unavailable")));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("TransportFailure", result.ErrorCode);
    }

    [Fact]
    public async Task Client_timeout_is_retryable()
    {
        var client = Client(new StubHandler((_, _) =>
            throw new TaskCanceledException("timeout")));

        var result = await client.DeliverAsync(Envelope());

        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.RetryableFailure,
            result.Disposition);
        Assert.Equal("RequestTimeout", result.ErrorCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        var client = Client(new StubHandler((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.DeliverAsync(Envelope(), cancellation.Token));
    }

    private static HttpBranchCloudOutboxDeliveryClient Client(
        HttpMessageHandler handler) =>
        new(new HttpClient(handler), Endpoint);

    private static BranchCloudOutboxEventEnvelope Envelope() =>
        BranchCloudOutboxEventEnvelope.Create(
            "OUTBOX-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            TableSessionId.From("SESSION-01"),
            TableSessionAction.SubmitOrder,
            3,
            new DateTimeOffset(2026, 10, 29, 4, 0, 0, TimeSpan.Zero));

    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value, JsonOptions()),
                Encoding.UTF8,
                "application/json")
        };

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _handler;

        public StubHandler(
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _content;

        public UnknownLengthContent(byte[] content)
        {
            _content = content;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(_content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
