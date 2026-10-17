using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Printing;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class PrintSpoolRetentionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registration_binds_disabled_configuration_and_adds_hosted_service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrintSpoolRetention:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPrintSpoolRetention(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PrintSpoolRetentionOptions>>().Value;
        Assert.False(options.Enabled);
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is PrintSpoolRetentionService);
    }

    [Fact]
    public async Task Disabled_service_requires_no_processor_and_does_no_work()
    {
        var processor = new RecordingProcessor();
        var service = Service(new PrintSpoolRetentionOptions(), [processor]);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, processor.CallCount);
    }

    [Fact]
    public async Task Enabled_service_fails_closed_without_exactly_one_processor()
    {
        var options = EnabledOptions();
        var missing = Service(options, []);
        var duplicate = Service(
            options,
            [new RecordingProcessor(), new RecordingProcessor()]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => missing.StartAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => duplicate.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Enabled_service_runs_immediately_with_explicit_policy()
    {
        var processor = new RecordingProcessor();
        var service = Service(EnabledOptions(), [processor]);

        await service.StartAsync(CancellationToken.None);
        await processor.Called.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(Now.AddDays(-30), processor.DeleteBefore);
        Assert.Equal(5_368_709_120, processor.MaximumTotalBytes);
        Assert.Equal(1_000, processor.MaximumDeletes);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public async Task Service_retries_after_cycle_failure_only_after_interval()
    {
        var processor = new FailsOnceProcessor();
        var options = new PrintSpoolRetentionOptions
        {
            Enabled = true,
            TerminalPayloadRetention = TimeSpan.FromDays(30),
            MaximumTotalBytes = 5_368_709_120,
            MaximumDeletesPerRun = 1_000,
            RunInterval = TimeSpan.FromMilliseconds(1)
        };
        var service = Service(options, [processor]);

        await service.StartAsync(CancellationToken.None);
        await processor.Succeeded.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(processor.CallCount >= 2);
    }

    [Fact]
    public void Validator_requires_every_policy_value_only_when_enabled()
    {
        var validator = new PrintSpoolRetentionOptionsValidator();

        var disabled = validator.Validate(null, new PrintSpoolRetentionOptions());
        var invalid = validator.Validate(null, new PrintSpoolRetentionOptions
        {
            Enabled = true,
            TerminalPayloadRetention = TimeSpan.Zero,
            MaximumTotalBytes = 0,
            MaximumDeletesPerRun = 0,
            RunInterval = TimeSpan.Zero
        });
        var valid = validator.Validate(null, EnabledOptions());

        Assert.True(disabled.Succeeded);
        Assert.True(invalid.Failed);
        Assert.Equal(4, invalid.Failures.Count());
        Assert.True(valid.Succeeded);
    }

    private static PrintSpoolRetentionOptions EnabledOptions() =>
        new()
        {
            Enabled = true,
            TerminalPayloadRetention = TimeSpan.FromDays(30),
            MaximumTotalBytes = 5_368_709_120,
            MaximumDeletesPerRun = 1_000,
            RunInterval = TimeSpan.FromHours(1)
        };

    private static PrintSpoolRetentionService Service(
        PrintSpoolRetentionOptions options,
        IEnumerable<IPrintSpoolRetentionProcessor> processors) =>
        new(
            Options.Create(options),
            processors,
            new FixedTimeProvider(Now),
            NullLogger<PrintSpoolRetentionService>.Instance);

    private sealed class RecordingProcessor : IPrintSpoolRetentionProcessor
    {
        private readonly TaskCompletionSource _called = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public Task Called => _called.Task;

        public int CallCount => Volatile.Read(ref _callCount);

        public DateTimeOffset DeleteBefore { get; private set; }

        public long MaximumTotalBytes { get; private set; }

        public int MaximumDeletes { get; private set; }

        public Task<RenderedPrintPayloadPruneResult> PruneAsync(
            DateTimeOffset deleteBefore,
            long maximumTotalBytes,
            int maximumDeletes,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            DeleteBefore = deleteBefore;
            MaximumTotalBytes = maximumTotalBytes;
            MaximumDeletes = maximumDeletes;
            _called.TrySetResult();
            var snapshot = new RenderedPrintPayloadRetentionSnapshot(0, 0, 0, 0, 0, 0, 0, 0);
            return Task.FromResult(new RenderedPrintPayloadPruneResult(
                snapshot,
                snapshot,
                0,
                0,
                true));
        }
    }

    private sealed class FailsOnceProcessor : IPrintSpoolRetentionProcessor
    {
        private readonly TaskCompletionSource _succeeded = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public Task Succeeded => _succeeded.Task;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<RenderedPrintPayloadPruneResult> PruneAsync(
            DateTimeOffset deleteBefore,
            long maximumTotalBytes,
            int maximumDeletes,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                throw new IOException("simulated retention failure");
            }

            _succeeded.TrySetResult();
            var snapshot = new RenderedPrintPayloadRetentionSnapshot(0, 0, 0, 0, 0, 0, 0, 0);
            return Task.FromResult(new RenderedPrintPayloadPruneResult(
                snapshot,
                snapshot,
                0,
                0,
                true));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
