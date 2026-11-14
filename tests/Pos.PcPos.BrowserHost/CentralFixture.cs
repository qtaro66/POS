using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Synchronization;

internal sealed class CentralFixture : IBranchCloudEventIngestionStore
{
    internal const string Origin = "https://127.0.0.1:17444";
    private readonly List<BranchCloudOutboxEventEnvelope> events = [];
    private bool cashAcknowledgmentLost;

    internal static async Task RunAsync(string directory)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Central fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
        File.WriteAllText(Path.Combine(directory, "central-cert-hash"), certificate.GetCertHashString(HashAlgorithmName.SHA256));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 17444, listen => listen.UseHttps(certificate)));
        var store = new CentralFixture();
        var service = new BranchCloudEventIngestionService(store);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(CentralCloudClaimTypes.BranchId, "BROWSER-TEST")], "Fixture"));
        await using var app = builder.Build();
        app.MapPost(BranchCloudEventIngestionEndpointRouteBuilderExtensions.RoutePattern,
            async (BranchCloudEventIngestionRequest body, CancellationToken token) =>
            {
                var result = await BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(principal, body, service, token);
                if (result.Result is Microsoft.AspNetCore.Http.HttpResults.Ok<BranchCloudEventIngestionResponse> &&
                    body.Action == "CompletePayment" && store.DropFirstCashAcknowledgment())
                {
                    return (IResult)Results.StatusCode(503);
                }
                return result;
            });
        app.MapGet("/fixture-events", () =>
        {
            lock (store.events)
            {
                return new
                {
                    Events = store.events.Select(item => new { item.EventId, item.BranchSequence, item.RequestId, Action = item.Action.ToString() }).ToArray(),
                    store.Duplicates
                };
            }
        });
        await app.RunAsync();
    }

    private int Duplicates { get; set; }

    private bool DropFirstCashAcknowledgment()
    {
        lock (events)
        {
            if (cashAcknowledgmentLost) { return false; }
            cashAcknowledgmentLost = true;
            return true;
        }
    }

    public Task<BranchCloudEventPersistenceResult> PersistAsync(BranchCloudOutboxEventEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (events)
        {
            var disposition = BranchCloudEventPersistenceDisposition.Stored;
            if (envelope.BranchSequence <= events.Count)
            {
                if (System.Text.Json.JsonSerializer.Serialize(events[(int)envelope.BranchSequence - 1]) != System.Text.Json.JsonSerializer.Serialize(envelope)) { throw new InvalidOperationException("Conflicting fixture event."); }
                Duplicates++;
                disposition = BranchCloudEventPersistenceDisposition.AlreadyStored;
            }
            else
            {
                if (envelope.BranchSequence != events.Count + 1)
                {
                    throw new BranchCloudEventSequenceGapException(envelope.BranchId, events.Count + 1, envelope.BranchSequence);
                }
                events.Add(envelope);
            }
            return Task.FromResult(new BranchCloudEventPersistenceResult(disposition, envelope.EventId, envelope.BranchId, envelope.BranchSequence));
        }
    }

    internal sealed class Transport(string directory) : IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
    {
        public HttpMessageHandler CreateHandler() => new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (message, certificate, _, _) =>
                message.RequestUri?.GetLeftPart(UriPartial.Authority) == Origin &&
                certificate is not null &&
                File.Exists(Path.Combine(directory, "central-cert-hash")) &&
                certificate.GetCertHashString(HashAlgorithmName.SHA256) == File.ReadAllText(Path.Combine(directory, "central-cert-hash"))
        };
    }
}
