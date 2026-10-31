using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudHostedRecoveryIntegrationTests
{
    [Fact]
    public async Task Host_shutdown_preserves_inflight_replay_and_reconstructed_host_resumes_after_expiry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-hosted-recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "branch.db");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync(deadline.Token);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
            await connection.OpenAsync(deadline.Token);
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
                await seed.ExecuteNonQueryAsync(deadline.Token);
            }
            var clock = new Clock();
            await new SqliteBranchCloudOutboxSequenceReplayStore(path).RequestAsync(
                new("RECOVERY-01", "BRANCH-01", 6, 7, clock.Now), deadline.Token);
            var sequences = new ConcurrentQueue<long>();
            var paused = new TransportFactory(sequences, pause: true);
            using (var host = CreateHost(path, clock, paused))
            {
                try
                {
                    await host.StartAsync(deadline.Token);
                    await paused.Started.Task.WaitAsync(deadline.Token);
                }
                finally
                {
                    await host.StopAsync(deadline.Token);
                }
            }
            Assert.Equal(new long[] { 6 }, sequences.ToArray());
            Assert.NotNull(Scalar(connection, "SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
            Assert.Null(Scalar(connection, "SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
            Assert.Equal("0", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_failures;"));
            Assert.Null(Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 7;"));

            clock.Now = clock.Now.AddSeconds(30);
            using (var host = CreateHost(path, clock, new TransportFactory(sequences, pause: false)))
            {
                try
                {
                    await host.StartAsync(deadline.Token);
                    while (Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 7;") is null)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token);
                    }
                }
                finally
                {
                    await host.StopAsync(deadline.Token);
                }
            }

            Assert.Equal(new long[] { 6, 6, 7 }, sequences.ToArray());
            Assert.NotNull(Scalar(connection, "SELECT completed_at FROM cloud_sync_sequence_replay_requests;"));
            Assert.Null(Scalar(connection, "SELECT lease_owner FROM cloud_sync_sequence_replay_leases;"));
            Assert.Equal("2026-10-31T03:05:00Z", Scalar(connection, "SELECT successfully_sent_at FROM cloud_sync_outbox WHERE branch_sequence = 6;"));
            Assert.Equal("0", Scalar(connection, "SELECT CAST(COUNT(*) AS TEXT) FROM cloud_sync_sequence_replay_failures;"));
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pos-hosted-recovery")) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Unsafe fixture path."); }
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static IHost CreateHost(string path, Clock clock, TransportFactory transport)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BranchCloudOutboxDelivery:Enabled"] = "true",
            ["BranchCloudOutboxDelivery:BranchId"] = "BRANCH-01",
            ["BranchCloudOutboxDelivery:DatabasePath"] = path,
            ["BranchCloudOutboxDelivery:ServiceOrigin"] = "https://cloud.example.test",
            ["BranchCloudOutboxDelivery:DeliveryPath"] = "/events",
            ["BranchCloudOutboxDelivery:IdleDelay"] = "00:00:00.010"
        });
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IBranchCloudDeliveryAuthenticatedHttpHandlerFactory>(transport);
        builder.Services.AddBranchCloudOutboxDelivery(builder.Configuration);
        return builder.Build();
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

    private sealed class TransportFactory(ConcurrentQueue<long> sequences, bool pause)
        : IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HttpMessageHandler CreateHandler() => new Handler(this, sequences, pause);

        private sealed class Handler(TransportFactory factory, ConcurrentQueue<long> sequences, bool pause)
            : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.Equal("https://cloud.example.test/events", request.RequestUri?.AbsoluteUri);
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var sequence = payload.RootElement.GetProperty("branchSequence").GetInt64();
                Assert.Equal($"EVENT-{sequence}", payload.RootElement.GetProperty("eventId").GetString());
                sequences.Enqueue(sequence);
                factory.Started.TrySetResult();
                if (pause) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new BranchCloudEventIngestionResponse(1, $"EVENT-{sequence}", "BRANCH-01", sequence))
                };
            }
        }
    }
}
