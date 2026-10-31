using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudMultipleGapRecoveryIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successive_gaps_replay_retained_events_before_normal_delivery_completes(bool transientFailure)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-multiple-gap-recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "branch.db");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync(cancellation.Token);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
            await connection.OpenAsync(cancellation.Token);
            using (var seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO cloud_sync_outbox (
                        outbox_id, branch_id, branch_sequence, request_id, table_session_id,
                        action, table_version, created_at, processing_status, retry_count, successfully_sent_at)
                    VALUES ('EVENT-6', 'BRANCH-01', 6, 'REQUEST-6', 'SESSION-01',
                        'SubmitOrder', 2, '2026-10-31T03:00:00Z', 'Completed', 0, '2026-10-31T03:05:00Z'),
                        ('EVENT-7', 'BRANCH-01', 7, 'REQUEST-7', 'SESSION-01',
                        'SubmitOrder', 3, '2026-10-31T03:10:00Z', 'Completed', 0, '2026-10-31T03:15:00Z'),
                        ('EVENT-8', 'BRANCH-01', 8, 'REQUEST-8', 'SESSION-01',
                        'SubmitOrder', 4, '2026-10-31T03:20:00Z', 'Pending', 0, NULL);
                    """;
                await seed.ExecuteNonQueryAsync(cancellation.Token);
            }
            var clock = new Clock();
            using var handler = new CentralStub(transientFailure);
            using var http = new HttpClient(handler);
            var client = new HttpBranchCloudOutboxDeliveryClient(http, new Uri("https://cloud.example.test/events"), clock);
            var requests = new SqliteBranchCloudOutboxSequenceReplayStore(path);
            var policy = new ExponentialBranchCloudOutboxRetryDelayPolicy();
            var scheduler = new BranchCloudReplayFirstCycleProcessor(
                new BranchCloudReplayCycleProcessor(
                    new SqliteBranchCloudOutboxSequenceReplayClaimStore(path),
                    new SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore(path), client,
                    new SqliteBranchCloudOutboxSequenceReplayCompletionStore(path),
                    new SqliteBranchCloudOutboxSequenceReplayFailureStore(path), requests,
                    policy, TimeSpan.FromMinutes(5), clock),
                new BranchCloudOutboxCycleProcessor(
                    new SqliteBranchCloudOutboxClaimStore(path),
                    new SqliteBranchCloudOutboxLeaseRenewalStore(path), client,
                    new SqliteBranchCloudDeliveryConfirmationStore(path),
                    new SqliteBranchCloudOutboxDeliveryFailureStore(path), requests,
                    policy, TimeSpan.FromMinutes(5), clock));
            Task<BranchCloudOutboxCycleResult> Cycle() => scheduler.ProcessNextAsync(
                "BRANCH-01", "TEST-WORKER", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), cancellation.Token);

            // Bound the loop so a recovery regression cannot leave the test polling indefinitely.
            for (var cycle = 0; cycle < 8 && handler.LastAccepted < 8; cycle++)
            {
                await Cycle();
                clock.Now = clock.Now.AddSeconds(5);
            }

            Assert.Equal(8, handler.LastAccepted);
            Assert.Equal(transientFailure ? new long[] { 8, 6, 8, 7, 7, 8 } : [8L, 6L, 8L, 7L, 8L], handler.Sequences);
            Assert.Equal("2026-10-31T03:05:00Z", Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 6;"));
            Assert.Equal("2026-10-31T03:15:00Z", Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 7;"));
            Assert.NotNull(Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 8;"));
            Assert.Equal("2", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_requests WHERE completed_at IS NOT NULL;"));
            Assert.Equal("0", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_requests WHERE completed_at IS NULL;"));
            Assert.Equal("0", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_leases WHERE lease_owner IS NOT NULL;"));
            Assert.Equal(transientFailure ? "1" : "0", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_failures;"));
            Assert.Equal(BranchCloudOutboxCycleDisposition.NoWork, (await Cycle()).Disposition);
            Assert.Equal(transientFailure ? 6 : 5, handler.Sequences.Count);
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pos-multiple-gap-recovery")) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Unsafe fixture path."); }
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 31, 4, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CentralStub(bool transientFailure) : HttpMessageHandler
    {
        private bool _failed;
        public long LastAccepted { get; private set; } = 5;
        public List<long> Sequences { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var data = payload.RootElement;
            var sequence = data.GetProperty("branchSequence").GetInt64();
            Assert.Equal($"EVENT-{sequence}", data.GetProperty("eventId").GetString());
            Assert.Equal("BRANCH-01", data.GetProperty("branchId").GetString());
            Sequences.Add(sequence);
            if (sequence > LastAccepted + 1)
            {
                return new(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new BranchCloudEventSequenceGapResponse(1, "BranchSequenceGap", "BRANCH-01", LastAccepted + 1, sequence))
                };
            }
            if (sequence == 7 && transientFailure && !_failed)
            {
                _failed = true;
                return new(HttpStatusCode.ServiceUnavailable);
            }
            Assert.Equal(LastAccepted + 1, sequence);
            LastAccepted = sequence;
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BranchCloudEventIngestionResponse(1, $"EVENT-{sequence}", "BRANCH-01", sequence))
            };
        }
    }
}
