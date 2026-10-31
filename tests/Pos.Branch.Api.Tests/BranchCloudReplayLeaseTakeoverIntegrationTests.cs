using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudReplayLeaseTakeoverIntegrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Replaced_worker_cannot_write_completion_or_failure_while_new_owner_is_delivering(
        bool staleAcknowledgment)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-replay-takeover", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "branch.db");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<BranchCloudOutboxCycleResult>? oldCycle = null;
        Task<BranchCloudOutboxCycleResult>? newCycle = null;
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
                        'SubmitOrder', 2, '2026-10-31T03:00:00Z', 'Completed', 0, '2026-10-31T03:05:00Z');
                    """;
                await seed.ExecuteNonQueryAsync(cancellation.Token);
            }
            var clock = new Clock();
            var oldClock = new Clock();
            await new SqliteBranchCloudOutboxSequenceReplayStore(path).RequestAsync(
                new("RECOVERY-01", "BRANCH-01", 6, 7, clock.Now), cancellation.Token);
            using var oldHandler = new PausedResponse(staleAcknowledgment);
            using var newHandler = new PausedResponse(acknowledged: true);
            using var oldHttp = new HttpClient(oldHandler);
            using var newHttp = new HttpClient(newHandler);
            BranchCloudReplayCycleProcessor Processor(HttpClient http, TimeProvider time) =>
                new(new SqliteBranchCloudOutboxSequenceReplayClaimStore(path),
                    new SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore(path),
                    new HttpBranchCloudOutboxDeliveryClient(http, new Uri("https://cloud.example.test/events"), time),
                    new SqliteBranchCloudOutboxSequenceReplayCompletionStore(path),
                    new SqliteBranchCloudOutboxSequenceReplayFailureStore(path),
                    new SqliteBranchCloudOutboxSequenceReplayStore(path),
                    new ExponentialBranchCloudOutboxRetryDelayPolicy(), TimeSpan.FromMinutes(5), time,
                    // Model a suspended worker whose heartbeat does not run before takeover.
                    heartbeatDelay: (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
            Task<BranchCloudOutboxCycleResult> Cycle(HttpClient http, string owner, TimeProvider time) =>
                Processor(http, time).ProcessNextAsync("BRANCH-01", owner,
                    TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), cancellation.Token);

            oldCycle = Cycle(oldHttp, "OLD-WORKER", oldClock);
            await oldHandler.Started.Task.WaitAsync(cancellation.Token);
            clock.Now = clock.Now.AddSeconds(30);
            // Keep the old clock behind so persistence fencing is exercised independently of time validation.
            newCycle = Cycle(newHttp, "NEW-WORKER", clock);
            await newHandler.Started.Task.WaitAsync(cancellation.Token);
            Assert.Equal("NEW-WORKER", Scalar(connection, "SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));

            oldHandler.Release.TrySetResult();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => oldCycle);
            Assert.Contains("replaced", error.Message, StringComparison.Ordinal);
            Assert.Equal("NEW-WORKER", Scalar(connection, "SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
            Assert.Null(Scalar(connection, "SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
            Assert.Equal("0", Scalar(connection, "SELECT CAST(retry_count AS TEXT) FROM cloud_sync_sequence_replay_leases;"));
            Assert.Equal("0", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_failures;"));

            newHandler.Release.TrySetResult();
            Assert.Equal(BranchCloudOutboxCycleDisposition.Processed, (await newCycle).Disposition);
            Assert.NotNull(Scalar(connection, "SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
            Assert.Null(Scalar(connection, "SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
            Assert.Equal("2026-10-31T03:05:00Z", Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox;"));
            Assert.Equal(BranchCloudOutboxCycleDisposition.NoWork, (await Cycle(newHttp, "NEW-WORKER", clock)).Disposition);
            Assert.Equal(1, oldHandler.Calls);
            Assert.Equal(1, newHandler.Calls);
        }
        finally
        {
            await cancellation.CancelAsync();
            // Drain in-flight operations before removing their SQLite fixture, including on assertion failure.
            foreach (var cycle in new[] { oldCycle, newCycle })
            {
                if (cycle is null) { continue; }
                try { await cycle; }
                catch (Exception) when (cycle.IsCanceled || cycle.IsFaulted) { }
            }
            var resolved = Path.GetFullPath(directory);
            var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pos-replay-takeover")) + Path.DirectorySeparatorChar;
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

    private sealed class PausedResponse(bool acknowledged) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("EVENT-6", payload.RootElement.GetProperty("eventId").GetString());
            Assert.Equal(6, payload.RootElement.GetProperty("branchSequence").GetInt64());
            Assert.Equal("BRANCH-01", payload.RootElement.GetProperty("branchId").GetString());
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return acknowledged
                ? new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new BranchCloudEventIngestionResponse(1, "EVENT-6", "BRANCH-01", 6))
                }
                : new(HttpStatusCode.ServiceUnavailable);
        }
    }
}
