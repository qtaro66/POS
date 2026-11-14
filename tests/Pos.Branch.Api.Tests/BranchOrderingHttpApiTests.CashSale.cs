using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cash_sale_requires_an_open_shift_and_replays_without_a_second_payment(bool failPrinting)
    {
        await using var fixture = await Fixture.CreateAsync();
        var cashier = Principal();
        cashier.AddIdentity(new ClaimsIdentity([new Claim(BranchPosClaimTypes.DeviceRole, "PcPos")],
            BranchEmployeeSessionAuthenticationDefaults.SchemeName));
        Assert.Equal(200, (await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"))).Status);
        Assert.Equal(200, (await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"))).Status);
        var detail = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1");
        var payment = await fixture.Call("RequestBranchTablePayment", new
        {
            schemaVersion = 1,
            context = Context("PAY-1", "SESSION-1"),
            expectedVersion = detail.Body.GetProperty("session").GetProperty("tableVersion").GetInt64()
        });
        Assert.Equal(200, payment.Status);
        var request = new
        {
            schemaVersion = 1,
            context = Context("CASH-1", "SESSION-1"),
            expectedVersion = payment.Body.GetProperty("result").GetProperty("tableVersion").GetInt64(),
            tenderedAmount = 300m
        };
        Assert.Equal(409, (await fixture.Call("CompleteBranchCashSale", request, principal: cashier)).Status);
        var store = new SqliteTableSessionCommandStore(fixture.Path);
        Assert.True(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-1", "STORE-1", new DateOnly(2026, 10, 31), 500m, default));
        Assert.False(await store.OpenTestCashCycleAsync("BRANCH-01", "SHIFT-2", "STORE-2", new DateOnly(2026, 10, 31), 500m, default));
        Assert.Equal(403, (await fixture.Call("CompleteBranchCashSale", request)).Status);
        var first = await fixture.Call("CompleteBranchCashSale", request, principal: cashier);
        Assert.Equal(200, first.Status);
        Assert.Equal(220m, first.Body.GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(80m, first.Body.GetProperty("changeAmount").GetDecimal());
        Assert.Equal("SHIFT-1", first.Body.GetProperty("shiftId").GetString());
        var replay = await fixture.Call("CompleteBranchCashSale", request, principal: cashier);
        Assert.Equal(first.Body.GetRawText(), replay.Body.GetRawText());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cash_sales;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cloud_sync_outbox WHERE action = 'CompletePayment' AND request_id = 'CASH-1' AND successfully_sent_at IS NULL;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM table_session_audit WHERE action = 'CompletePayment' AND request_id = 'CASH-1';"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM simulated_cash_receipt_jobs;"));
        Assert.Equal("QueuedForSimulation", replay.Body.GetProperty("printStatus").GetString());
        var receiptNumber = first.Body.GetProperty("receiptNumber").GetString()!;
        Assert.Contains("220.00", await store.RenderTestReceiptAsync("BRANCH-01", receiptNumber, default));
        Assert.Null(await store.RenderTestReceiptAsync("BRANCH-02", receiptNumber, default));
        var simulation = await store.SimulateReceiptAsync("BRANCH-01", receiptNumber, failPrinting, default);
        Assert.NotNull(simulation);
        Assert.Equal(failPrinting ? "Failed" : "Simulated", simulation.Status);
        Assert.Equal(simulation, await store.SimulateReceiptAsync("BRANCH-01", receiptNumber, !failPrinting, default));
        Assert.Null(await store.SimulateReceiptAsync("BRANCH-02", receiptNumber, false, default));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM test_receipt_simulation_results;"));
        Assert.Equal(720m, await new SqliteTableSessionCommandStore(fixture.Path).ReadExpectedTestCashAsync("BRANCH-01", 500m, default));
        Assert.Equal("Completed", await fixture.Scalar("SELECT status FROM table_sessions;"));
        Assert.Null(await store.CloseTestCashCycleAsync("BRANCH-01", "OTHER", 710m, default));
        var closedAt = DateTimeOffset.UnixEpoch;
        Assert.Equal(new TestCashClose(720m, 710m, -10m, "EMP-1", closedAt),
            await store.CloseTestCashCycleAsync("BRANCH-01", "SHIFT-1", 710m, default, "EMP-1", closedAt));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM test_cash_close_history;"));
        Assert.Null(await store.ReadExpectedTestCashAsync("BRANCH-01", 500m, default));
        await VerifyCashReconnectAsync(fixture);
    }

    private static async Task VerifyCashReconnectAsync(Fixture fixture)
    {
        var clock = new ReconnectClock();
        using var central = new ReconnectingCentral();
        using var http = new HttpClient(central);
        BranchCloudOutboxCycleProcessor Processor() => new(
            new SqliteBranchCloudOutboxClaimStore(fixture.Path),
            new SqliteBranchCloudOutboxLeaseRenewalStore(fixture.Path),
            new HttpBranchCloudOutboxDeliveryClient(http, new Uri("https://central.example.test/events"), clock),
            new SqliteBranchCloudDeliveryConfirmationStore(fixture.Path),
            new SqliteBranchCloudOutboxDeliveryFailureStore(fixture.Path),
            new SqliteBranchCloudOutboxSequenceReplayStore(fixture.Path),
            new ExponentialBranchCloudOutboxRetryDelayPolicy(), TimeSpan.FromMinutes(1), clock);
        Task<BranchCloudOutboxCycleResult> Cycle(BranchCloudOutboxCycleProcessor processor) => processor.ProcessNextAsync(
            "BRANCH-01", "TEST-WORKER", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

        await Cycle(Processor());
        Assert.Empty(central.Committed);
        Assert.Equal("Completed", await fixture.Scalar("SELECT status FROM table_sessions;"));
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM simulated_cash_receipt_jobs;"));

        central.Online = true;
        var restartedProcessor = Processor();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            clock.Now = clock.Now.AddMinutes(2);
            if ((await Cycle(restartedProcessor)).Disposition == BranchCloudOutboxCycleDisposition.NoWork) { break; }
        }

        Assert.Equal(new long[] { 1, 2, 3, 4 }, central.Committed.Keys.ToArray());
        Assert.Equal(new long[] { 4, 4 }, central.CashAttempts.ToArray());
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cloud_sync_outbox WHERE request_id = 'CASH-1' AND successfully_sent_at IS NOT NULL;"));
        Assert.Equal("0", await fixture.Scalar("SELECT COUNT(*) FROM cloud_sync_outbox WHERE successfully_sent_at IS NULL;"));
    }

    private sealed class ReconnectClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow.AddMinutes(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ReconnectingCentral : HttpMessageHandler
    {
        public bool Online { get; set; }
        public SortedDictionary<long, string> Committed { get; } = [];
        public List<long> CashAttempts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Online) { throw new HttpRequestException("Central is offline."); }
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var data = payload.RootElement;
            var sequence = data.GetProperty("branchSequence").GetInt64();
            var id = data.GetProperty("eventId").GetString()!;
            if (!Committed.TryAdd(sequence, id)) { Assert.Equal(Committed[sequence], id); }
            if (data.GetProperty("requestId").GetString() == "CASH-1")
            {
                CashAttempts.Add(sequence);
                if (CashAttempts.Count == 1) { throw new HttpRequestException("Acknowledgment lost after commit."); }
            }

            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BranchCloudEventIngestionResponse(1, id, "BRANCH-01", sequence))
            };
        }
    }
}
