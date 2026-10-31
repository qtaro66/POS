using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Central.Api.Tests;

public sealed class BranchCentralCloudHttpInteroperabilityTests
{
    [Fact]
    public async Task Real_central_gap_and_acknowledgment_results_are_understood_by_branch_client()
    {
        var store = new InMemoryStore();
        using var handler = new CentralHandler(store, loseFirstAcknowledgment: false);
        using var http = new HttpClient(handler);
        var client = Client(http);

        var gap = await client.DeliverAsync(Envelope(2));
        Assert.Equal(BranchCloudOutboxDeliveryDisposition.RetryableFailure, gap.Disposition);
        Assert.Equal("BranchSequenceGap", gap.ErrorCode);
        Assert.Equal(1, gap.ExpectedBranchSequence);
        Assert.Empty(store.Events);

        AssertAcknowledged(await client.DeliverAsync(Envelope(1)), 1);
        AssertAcknowledged(await client.DeliverAsync(Envelope(1)), 1);
        AssertAcknowledged(await client.DeliverAsync(Envelope(2)), 2);

        Assert.Equal(2, store.Events.Count);
        Assert.Equal(1, store.DuplicateCount);
        Assert.Equal(new[] { HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK }, handler.Statuses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_delivery_preserves_event_identity_with_or_without_lost_acknowledgment(bool loseAcknowledgment)
    {
        var store = new InMemoryStore();
        using var handler = new CentralHandler(store, loseAcknowledgment);
        using var http = new HttpClient(handler);
        var client = Client(http);

        var first = await client.DeliverAsync(Envelope(1));
        if (loseAcknowledgment)
        {
            Assert.Equal(BranchCloudOutboxDeliveryDisposition.RetryableFailure, first.Disposition);
            Assert.Equal("HttpStatus503", first.ErrorCode);
        }
        else
        {
            AssertAcknowledged(first, 1);
        }
        Assert.Single(store.Events);
        AssertAcknowledged(await client.DeliverAsync(Envelope(1)), 1);
        Assert.Single(store.Events);
        Assert.Equal(1, store.DuplicateCount);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK }, handler.Statuses);
    }

    private static HttpBranchCloudOutboxDeliveryClient Client(HttpClient http) =>
        new(http, new Uri("https://central.example.test/events"));

    private static BranchCloudOutboxEventEnvelope Envelope(long sequence) =>
        BranchCloudOutboxEventEnvelope.Create(
            $"EVENT-{sequence}", "BRANCH-01", sequence, $"REQUEST-{sequence}",
            TableSessionId.From("SESSION-01"), TableSessionAction.SubmitOrder, sequence,
            new DateTimeOffset(2026, 10, 31, 4, 0, 0, TimeSpan.Zero));

    private static void AssertAcknowledged(BranchCloudOutboxDeliveryResult result, long sequence)
    {
        Assert.Equal(BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged, result.Disposition);
        Assert.Equal($"EVENT-{sequence}", result.AcknowledgedEventId);
        Assert.Equal(sequence, result.AcknowledgedBranchSequence);
    }

    private sealed class CentralHandler(InMemoryStore store, bool loseFirstAcknowledgment) : HttpMessageHandler
    {
        public List<HttpStatusCode> Statuses { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadFromJsonAsync<BranchCloudEventIngestionRequest>(cancellationToken);
            Assert.NotNull(body);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(CentralCloudClaimTypes.BranchId, "BRANCH-01")], "SyntheticAuthentication"));
            var result = await BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
                principal, body, new BranchCloudEventIngestionService(store), cancellationToken);

            using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            await using var output = new MemoryStream();
            context.Response.Body = output;
            await result.ExecuteAsync(context);
            var status = (HttpStatusCode)context.Response.StatusCode;
            Statuses.Add(status);
            if (loseFirstAcknowledgment && Statuses.Count == 1 && status == HttpStatusCode.OK)
            {
                // Central has stored the event; simulate a proxy failure replacing its acknowledgment.
                return new(HttpStatusCode.ServiceUnavailable);
            }
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(output.ToArray()) };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Response.ContentType!);
            return response;
        }
    }

    private sealed class InMemoryStore : IBranchCloudEventIngestionStore
    {
        public Dictionary<long, BranchCloudOutboxEventEnvelope> Events { get; } = [];
        public int DuplicateCount { get; private set; }

        public Task<BranchCloudEventPersistenceResult> PersistAsync(
            BranchCloudOutboxEventEnvelope envelope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var disposition = BranchCloudEventPersistenceDisposition.Stored;
            if (Events.TryGetValue(envelope.BranchSequence, out var existing))
            {
                Assert.Equal(JsonSerializer.Serialize(existing), JsonSerializer.Serialize(envelope));
                DuplicateCount++;
                disposition = BranchCloudEventPersistenceDisposition.AlreadyStored;
            }
            else
            {
                if (envelope.BranchSequence > Events.Count + 1)
                {
                    throw new BranchCloudEventSequenceGapException(envelope.BranchId, Events.Count + 1, envelope.BranchSequence);
                }
                Events.Add(envelope.BranchSequence, envelope);
            }
            return Task.FromResult(new BranchCloudEventPersistenceResult(
                disposition, envelope.EventId, envelope.BranchId, envelope.BranchSequence));
        }
    }
}
