using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudOutboxHttpDeliveryCompositionTests
{
    [Fact]
    public async Task Named_pipeline_is_used_by_delivery_adapter()
    {
        var handler = new RecordingHandler();
        var services = Services();
        services.AddSingleton<
            IBranchCloudDeliveryAuthenticatedHttpHandlerFactory>(
            new RecordingHandlerFactory(handler));
        services.AddBranchCloudOutboxHttpDeliveryClient();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<
            IBranchCloudOutboxDeliveryClient>();
        var result = await client.DeliverAsync(Envelope());

        Assert.IsType<HttpBranchCloudOutboxDeliveryClient>(client);
        Assert.Equal(
            BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged,
            result.Disposition);
        Assert.Equal(
            "https://cloud.example.test/api/v1/branch-events",
            handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public void Missing_authenticated_handler_factory_fails_closed()
    {
        var services = Services();
        services.AddBranchCloudOutboxHttpDeliveryClient();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IBranchCloudOutboxDeliveryClient>());
    }

    [Fact]
    public void Null_authenticated_handler_fails_closed()
    {
        var services = Services();
        services.AddSingleton<
            IBranchCloudDeliveryAuthenticatedHttpHandlerFactory,
            NullHandlerFactory>();
        services.AddBranchCloudOutboxHttpDeliveryClient();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IBranchCloudOutboxDeliveryClient>());
    }

    [Fact]
    public void Existing_delivery_client_is_preserved()
    {
        var services = Services();
        services.AddSingleton<
            IBranchCloudOutboxDeliveryClient,
            CustomDeliveryClient>();

        services.AddBranchCloudOutboxHttpDeliveryClient();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomDeliveryClient>(
            provider.GetRequiredService<IBranchCloudOutboxDeliveryClient>());
    }

    [Fact]
    public void Duplicate_composition_keeps_one_delivery_registration()
    {
        var services = Services();

        services.AddBranchCloudOutboxHttpDeliveryClient();
        services.AddBranchCloudOutboxHttpDeliveryClient();

        Assert.Single(services, descriptor =>
            descriptor.ServiceType ==
                typeof(IBranchCloudOutboxDeliveryClient));
    }

    [Fact]
    public void Null_services_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudOutboxHttpDeliveryServiceCollectionExtensions
                .AddBranchCloudOutboxHttpDeliveryClient(null!));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<BranchCloudOutboxDeliveryOptions>>(
            Options.Create(
                new BranchCloudOutboxDeliveryOptions
                {
                    ServiceOrigin = "https://cloud.example.test",
                    DeliveryPath = "/api/v1/branch-events"
                }));
        return services;
    }

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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            var response = new BranchCloudEventIngestionResponse(
                1,
                "OUTBOX-01",
                "BRANCH-01",
                7);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        response,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class RecordingHandlerFactory :
        IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
    {
        private readonly HttpMessageHandler _handler;

        public RecordingHandlerFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpMessageHandler CreateHandler() => _handler;
    }

    private sealed class NullHandlerFactory :
        IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
    {
        public HttpMessageHandler CreateHandler() => null!;
    }

    private sealed class CustomDeliveryClient : IBranchCloudOutboxDeliveryClient
    {
        public Task<BranchCloudOutboxDeliveryResult> DeliverAsync(
            BranchCloudOutboxEventEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                BranchCloudOutboxDeliveryResult.Failed("NotUsed"));
    }
}
