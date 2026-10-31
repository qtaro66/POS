using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudOutboxDeliveryCompositionTests
{
    [Fact]
    public void Disabled_delivery_registers_no_stores_and_requires_no_database()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchCloudOutboxDelivery:Enabled"] = "false",
            ["BranchCloudOutboxDelivery:DatabasePath"] = string.Empty
        });

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Null(provider.GetService<IBranchCloudOutboxClaimStore>());
        Assert.Null(provider.GetService<IBranchCloudOutboxLeaseRenewalStore>());
        Assert.Null(provider.GetService<IBranchCloudOutboxDeliveryFailureStore>());
        Assert.Null(provider.GetService<IBranchCloudOutboxSequenceReplayStore>());
        Assert.Null(provider.GetService<IBranchCloudOutboxSequenceReplayClaimStore>());
        Assert.Null(provider.GetService<IBranchCloudOutboxSequenceReplayLeaseRenewalStore>());
        Assert.Null(provider.GetService<IBranchCloudOutboxSequenceReplayCompletionStore>());
        Assert.Null(provider.GetService<IBranchCloudOutboxSequenceReplayFailureStore>());
        Assert.Null(provider.GetService<IBranchCloudReplayCycleProcessor>());
        Assert.Null(provider.GetService<IBranchCloudDeliveryConfirmationStore>());
        Assert.IsType<SqliteBranchCloudOutboxReadinessProbe>(
            provider.GetRequiredService<IBranchCloudOutboxReadinessProbe>());
    }

    [Fact]
    public void Enabled_delivery_fails_closed_without_existing_database()
    {
        var missingDatabase = Path.Combine(
            Path.GetTempPath(),
            $"missing-cloud-outbox-{Guid.NewGuid():N}.db");
        using var provider = Provider(Enabled(missingDatabase));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "existing Branch database file",
                StringComparison.Ordinal));
        Assert.False(File.Exists(missingDatabase));
    }

    [Fact]
    public async Task Enabled_delivery_composes_all_persistence_stores_as_singletons()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-cloud-outbox-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            using var provider = Provider(
                Enabled(databasePath),
                services => services.AddSingleton<
                    IBranchCloudOutboxDeliveryClient,
                    AlwaysAcknowledgedClient>());

            provider.GetRequiredService<IStartupValidator>().Validate();

            AssertSingleton<IBranchCloudOutboxClaimStore,
                SqliteBranchCloudOutboxClaimStore>(provider);
            AssertSingleton<IBranchCloudOutboxLeaseRenewalStore,
                SqliteBranchCloudOutboxLeaseRenewalStore>(provider);
            AssertSingleton<IBranchCloudOutboxDeliveryFailureStore,
                SqliteBranchCloudOutboxDeliveryFailureStore>(provider);
            AssertSingleton<IBranchCloudOutboxSequenceReplayStore,
                SqliteBranchCloudOutboxSequenceReplayStore>(provider);
            AssertSingleton<IBranchCloudOutboxSequenceReplayClaimStore,
                SqliteBranchCloudOutboxSequenceReplayClaimStore>(provider);
            AssertSingleton<IBranchCloudOutboxSequenceReplayLeaseRenewalStore,
                SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore>(provider);
            AssertSingleton<IBranchCloudOutboxSequenceReplayCompletionStore,
                SqliteBranchCloudOutboxSequenceReplayCompletionStore>(provider);
            AssertSingleton<IBranchCloudOutboxSequenceReplayFailureStore,
                SqliteBranchCloudOutboxSequenceReplayFailureStore>(provider);
            AssertSingleton<IBranchCloudDeliveryConfirmationStore,
                SqliteBranchCloudDeliveryConfirmationStore>(provider);
            AssertSingleton<IBranchCloudOutboxCycleProcessor,
                BranchCloudReplayFirstCycleProcessor>(provider);
            AssertSingleton<IBranchCloudReplayCycleProcessor,
                BranchCloudReplayCycleProcessor>(provider);
            AssertSingleton<IBranchCloudOutboxRetryDelayPolicy,
                ExponentialBranchCloudOutboxRetryDelayPolicy>(provider);
            Assert.Contains(
                provider.GetServices<IHostedService>(),
                service => service is BranchCloudOutboxPollingService);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Enabled_delivery_composes_authenticated_http_client()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-cloud-outbox-http-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            using var provider = Provider(
                Enabled(databasePath),
                services => services.AddSingleton<
                    IBranchCloudDeliveryAuthenticatedHttpHandlerFactory,
                    TestAuthenticatedHandlerFactory>());

            provider.GetRequiredService<IStartupValidator>().Validate();

            Assert.IsType<HttpBranchCloudOutboxDeliveryClient>(
                provider.GetRequiredService<IBranchCloudOutboxDeliveryClient>());
            Assert.IsType<BranchCloudReplayFirstCycleProcessor>(
                provider.GetRequiredService<IBranchCloudOutboxCycleProcessor>());
            Assert.IsType<BranchCloudReplayCycleProcessor>(
                provider.GetRequiredService<IBranchCloudReplayCycleProcessor>());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Enabled_delivery_fails_closed_without_authenticated_handler()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-cloud-outbox-client-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            using var provider = Provider(Enabled(databasePath));
            var exception = Assert.Throws<OptionsValidationException>(() =>
                provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains(
                exception.Failures,
                failure => failure.Contains(
                    nameof(IBranchCloudDeliveryAuthenticatedHttpHandlerFactory),
                    StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Existing_cycle_processor_registration_is_preserved()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-cloud-outbox-processor-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            var custom = new CustomProcessor();
            using var provider = Provider(
                Enabled(databasePath),
                services => services.AddSingleton<IBranchCloudOutboxCycleProcessor>(custom));

            provider.GetRequiredService<IStartupValidator>().Validate();

            Assert.Same(
                custom,
                provider.GetRequiredService<IBranchCloudOutboxCycleProcessor>());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Existing_store_registrations_are_preserved()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-cloud-outbox-custom-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            var custom = new CustomStores();
            using var provider = Provider(
                Enabled(databasePath),
                services =>
                {
                    services.AddSingleton<
                        IBranchCloudDeliveryAuthenticatedHttpHandlerFactory,
                        TestAuthenticatedHandlerFactory>();
                    services.AddSingleton<IBranchCloudOutboxClaimStore>(custom);
                    services.AddSingleton<IBranchCloudOutboxLeaseRenewalStore>(custom);
                    services.AddSingleton<IBranchCloudOutboxDeliveryFailureStore>(custom);
                    services.AddSingleton<IBranchCloudOutboxSequenceReplayStore>(custom);
                    services.AddSingleton<IBranchCloudOutboxSequenceReplayClaimStore>(custom);
                    services.AddSingleton<IBranchCloudOutboxSequenceReplayLeaseRenewalStore>(custom);
                    services.AddSingleton<IBranchCloudOutboxSequenceReplayCompletionStore>(custom);
                    services.AddSingleton<IBranchCloudOutboxSequenceReplayFailureStore>(custom);
                    services.AddSingleton<IBranchCloudDeliveryConfirmationStore>(custom);
                });

            provider.GetRequiredService<IStartupValidator>().Validate();

            Assert.Same(custom, provider.GetRequiredService<IBranchCloudOutboxClaimStore>());
            Assert.Same(
                custom,
                provider.GetRequiredService<IBranchCloudOutboxLeaseRenewalStore>());
            Assert.Same(
                custom,
                provider.GetRequiredService<IBranchCloudOutboxDeliveryFailureStore>());
            Assert.Same(
                custom,
                provider.GetRequiredService<IBranchCloudOutboxSequenceReplayStore>());
            Assert.Same(custom, provider.GetRequiredService<IBranchCloudOutboxSequenceReplayClaimStore>());
            Assert.Same(custom, provider.GetRequiredService<IBranchCloudOutboxSequenceReplayLeaseRenewalStore>());
            Assert.Same(custom, provider.GetRequiredService<IBranchCloudOutboxSequenceReplayCompletionStore>());
            Assert.Same(custom, provider.GetRequiredService<IBranchCloudOutboxSequenceReplayFailureStore>());
            Assert.Same(
                custom,
                provider.GetRequiredService<IBranchCloudDeliveryConfirmationStore>());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void Existing_readiness_probe_is_preserved()
    {
        var custom = new AlwaysReadyProbe();
        using var provider = Provider(
            new Dictionary<string, string?>
            {
                ["BranchCloudOutboxDelivery:Enabled"] = "false"
            },
            services => services.AddSingleton<IBranchCloudOutboxReadinessProbe>(custom));

        Assert.Same(
            custom,
            provider.GetRequiredService<IBranchCloudOutboxReadinessProbe>());
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var configuration = Configuration(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudOutboxDeliveryServiceCollectionExtensions
                .AddBranchCloudOutboxDelivery(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBranchCloudOutboxDelivery(null!));
    }

    [Fact]
    public async Task Startup_rejects_missing_replay_structure_even_when_normal_outbox_is_ready()
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-startup-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false
                }.ToString()))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE cloud_sync_sequence_replay_failures;";
                await command.ExecuteNonQueryAsync();
            }
            using var provider = Provider(Enabled(path), services =>
                services.AddSingleton<IBranchCloudOutboxDeliveryClient, AlwaysAcknowledgedClient>());

            var error = Assert.Throws<OptionsValidationException>(() =>
                provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Contains(error.Failures, failure => failure.Contains("ReplayTablesMissing", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Existing_replay_processor_registration_is_preserved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"custom-replay-cycle-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            var custom = new CustomReplayProcessor();
            using var provider = Provider(Enabled(path), services =>
            {
                services.AddSingleton<IBranchCloudReplayCycleProcessor>(custom);
                services.AddSingleton<IBranchCloudOutboxCycleProcessor>(new CustomProcessor());
            });
            provider.GetRequiredService<IStartupValidator>().Validate();
            Assert.Same(custom, provider.GetRequiredService<IBranchCloudReplayCycleProcessor>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CustomReplayProcessor : IBranchCloudReplayCycleProcessor
    {
        public Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
            string branchId, string leaseOwner, TimeSpan leaseDuration,
            TimeSpan retryDelay, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Composition must not execute replay delivery.");
    }

    private static ServiceProvider Provider(
        IReadOnlyDictionary<string, string?> values,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddBranchCloudOutboxDelivery(Configuration(values));
        return services.BuildServiceProvider();
    }

    private static IReadOnlyDictionary<string, string?> Enabled(string databasePath) =>
        new Dictionary<string, string?>
        {
            ["BranchCloudOutboxDelivery:Enabled"] = "true",
            ["BranchCloudOutboxDelivery:DatabasePath"] = databasePath,
            ["BranchCloudOutboxDelivery:BranchId"] = "BRANCH-01",
            ["BranchCloudOutboxDelivery:WorkerId"] = "WORKER-01",
            ["BranchCloudOutboxDelivery:ServiceOrigin"] =
                "https://cloud.example.test",
            ["BranchCloudOutboxDelivery:DeliveryPath"] =
                "/test/cloud-events"
        };

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static void AssertSingleton<TService, TImplementation>(
        IServiceProvider provider)
        where TService : class
        where TImplementation : class, TService
    {
        var first = provider.GetRequiredService<TService>();
        Assert.IsType<TImplementation>(first);
        Assert.Same(first, provider.GetRequiredService<TService>());
    }

    private sealed class AlwaysReadyProbe : IBranchCloudOutboxReadinessProbe
    {
        public int ExpectedSchemaVersion => 18;

        public BranchCloudOutboxReadinessResult Check(string databasePath) =>
            new(true, 18, true, true, [], [], null);
    }

    private sealed class CustomStores :
        IBranchCloudOutboxClaimStore,
        IBranchCloudOutboxLeaseRenewalStore,
        IBranchCloudOutboxDeliveryFailureStore,
        IBranchCloudOutboxSequenceReplayStore,
        IBranchCloudOutboxSequenceReplayClaimStore,
        IBranchCloudOutboxSequenceReplayLeaseRenewalStore,
        IBranchCloudOutboxSequenceReplayCompletionStore,
        IBranchCloudOutboxSequenceReplayFailureStore,
        IBranchCloudDeliveryConfirmationStore
    {
        public Task<BranchCloudOutboxLease?> ClaimNextAsync(
            string branchId,
            string leaseOwner,
            DateTimeOffset claimedAt,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BranchCloudOutboxLease?>(null);

        public Task<BranchCloudOutboxLease> RenewAsync(
            BranchCloudOutboxLease lease,
            DateTimeOffset renewedAt,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(lease);

        public Task<BranchCloudOutboxDeliveryFailureResult> RecordAsync(
            BranchCloudOutboxDeliveryFailure failure,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BranchCloudOutboxDeliveryFailureResult(1, false));

        public Task<BranchCloudOutboxSequenceReplayResult> RequestAsync(
            BranchCloudOutboxSequenceReplayRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BranchCloudOutboxSequenceReplayResult.Requested);

        Task<BranchCloudOutboxSequenceReplayLease?> IBranchCloudOutboxSequenceReplayClaimStore.ClaimNextAsync(
            string branchId,
            string leaseOwner,
            DateTimeOffset claimedAt,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) =>
            Task.FromResult<BranchCloudOutboxSequenceReplayLease?>(null);

        public Task<BranchCloudOutboxSequenceReplayLease> RenewAsync(
            BranchCloudOutboxSequenceReplayLease lease,
            DateTimeOffset renewedAt,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) => Task.FromResult(lease);

        public Task<BranchCloudOutboxSequenceReplayCompletionResult> CompleteAsync(
            BranchCloudOutboxSequenceReplayCompletion completion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BranchCloudOutboxSequenceReplayCompletionResult.Completed);

        public Task<BranchCloudOutboxDeliveryFailureResult> RecordAsync(
            BranchCloudOutboxSequenceReplayFailure failure,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BranchCloudOutboxDeliveryFailureResult(1, false));

        public Task<BranchCloudDeliveryConfirmationResult> ConfirmAsync(
            BranchCloudDeliveryConfirmation confirmation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BranchCloudDeliveryConfirmationResult.Confirmed);
    }

    private sealed class AlwaysAcknowledgedClient : IBranchCloudOutboxDeliveryClient
    {
        public Task<BranchCloudOutboxDeliveryResult> DeliverAsync(
            BranchCloudOutboxEventEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                BranchCloudOutboxDeliveryResult.Acknowledged(
                    envelope.EventId,
                    envelope.BranchSequence));
    }

    private sealed class TestAuthenticatedHandlerFactory :
        IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
    {
        public HttpMessageHandler CreateHandler() => new TestHandler();

        private sealed class TestHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                throw new InvalidOperationException(
                    "The composition test must not contact a network endpoint.");
        }
    }

    private sealed class CustomProcessor : IBranchCloudOutboxCycleProcessor
    {
        public Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
            string branchId,
            string leaseOwner,
            TimeSpan leaseDuration,
            TimeSpan retryDelay,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new BranchCloudOutboxCycleResult(
                    BranchCloudOutboxCycleDisposition.NoWork));
    }
}
