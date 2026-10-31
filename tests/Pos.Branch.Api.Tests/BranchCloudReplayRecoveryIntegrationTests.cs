using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudReplayRecoveryIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sequence_gap_recovers_through_sqlite_http_mapping_and_serial_scheduler(bool transientFailure)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-recovery-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "branch.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
            await connection.OpenAsync();
            using (var seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO cloud_sync_outbox (
                        outbox_id, branch_id, branch_sequence, request_id, table_session_id,
                        action, table_version, created_at, processing_status, retry_count, successfully_sent_at)
                    VALUES ('EVENT-6', 'BRANCH-01', 6, 'REQUEST-6', 'SESSION-01',
                        'SubmitOrder', 2, '2026-10-31T03:00:00Z', 'Completed', 0, '2026-10-31T03:05:00Z'),
                        ('EVENT-7', 'BRANCH-01', 7, 'REQUEST-7', 'SESSION-01',
                        'SubmitOrder', 3, '2026-10-31T03:10:00Z', 'Pending', 0, NULL);
                    """;
                await seed.ExecuteNonQueryAsync();
            }
            var clock = new Clock();
            using var handler = new CentralStub(transientFailure);
            using var http = new HttpClient(handler);
            var client = new HttpBranchCloudOutboxDeliveryClient(http, new Uri("https://cloud.example.test/events"), clock);
            var policy = new ExponentialBranchCloudOutboxRetryDelayPolicy();
            var requests = new SqliteBranchCloudOutboxSequenceReplayStore(path);
            var replay = new BranchCloudReplayCycleProcessor(
                new SqliteBranchCloudOutboxSequenceReplayClaimStore(path),
                new SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore(path), client,
                new SqliteBranchCloudOutboxSequenceReplayCompletionStore(path),
                new SqliteBranchCloudOutboxSequenceReplayFailureStore(path), requests,
                policy, TimeSpan.FromMinutes(5), clock);
            var normal = new BranchCloudOutboxCycleProcessor(
                new SqliteBranchCloudOutboxClaimStore(path),
                new SqliteBranchCloudOutboxLeaseRenewalStore(path), client,
                new SqliteBranchCloudDeliveryConfirmationStore(path),
                new SqliteBranchCloudOutboxDeliveryFailureStore(path), requests,
                policy, TimeSpan.FromMinutes(5), clock);
            var scheduler = new BranchCloudReplayFirstCycleProcessor(replay, normal);
            Task<BranchCloudOutboxCycleResult> Cycle() => scheduler.ProcessNextAsync(
                "BRANCH-01", "TEST-WORKER", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

            await Cycle(); // Central rejects 7 and requests retained 6.
            Assert.Equal("1", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_requests WHERE completed_at IS NULL;"));
            await Cycle(); // Replay 6 either succeeds or schedules a retry.
            if (transientFailure)
            {
                Assert.Equal("1", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_failures;"));
                Assert.Equal("1", Scalar(connection, "SELECT CAST(retry_count AS TEXT) FROM cloud_sync_sequence_replay_leases;"));
                Assert.Equal(BranchCloudOutboxCycleDisposition.NoWork, (await Cycle()).Disposition);
                Assert.Equal(2, handler.Sequences.Count);
                clock.Now = clock.Now.AddSeconds(5);
                await Cycle(); // Eligible replay succeeds.
            }
            clock.Now = clock.Now.AddSeconds(5);
            await Cycle(); // Original 7 now succeeds.

            Assert.Equal(transientFailure ? new long[] { 7, 6, 6, 7 } : [7L, 6L, 7L], handler.Sequences);
            Assert.Equal("2026-10-31T03:05:00Z", Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 6;"));
            Assert.NotNull(Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 7;"));
            Assert.Equal("0", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_requests WHERE completed_at IS NULL;"));
            Assert.Null(Scalar(connection, "SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
            Assert.Equal(BranchCloudOutboxCycleDisposition.NoWork, (await Cycle()).Disposition);
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pos-recovery-integration")) + Path.DirectorySeparatorChar;
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
        private bool _recovered;
        private bool _failed;
        public List<long> Sequences { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var data = payload.RootElement;
            var sequence = data.GetProperty("branchSequence").GetInt64();
            var id = data.GetProperty("eventId").GetString()!;
            Sequences.Add(sequence);
            Assert.False(data.TryGetProperty("recoveryId", out _));
            if (sequence == 7 && !_recovered)
            {
                return new(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new BranchCloudEventSequenceGapResponse(1, "BranchSequenceGap", "BRANCH-01", 6, 7))
                };
            }
            if (sequence == 6)
            {
                Assert.Equal("EVENT-6", id);
                if (transientFailure && !_failed)
                {
                    _failed = true;
                    return new(HttpStatusCode.ServiceUnavailable);
                }
                _recovered = true;
            }
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BranchCloudEventIngestionResponse(1, id, "BRANCH-01", sequence))
            };
        }
    }
}
