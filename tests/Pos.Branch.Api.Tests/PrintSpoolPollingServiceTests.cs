using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Printing;

namespace Pos.Branch.Api.Tests;

public sealed class PrintSpoolPollingServiceTests
{
    [Fact]
    public void Registration_binds_safe_disabled_defaults_and_adds_hosted_service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrintSpoolPolling:Enabled"] = "false",
                ["PrintSpoolPolling:WorkerIdPrefix"] = "branch-test"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPrintSpoolPolling(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PrintSpoolPollingOptions>>().Value;
        Assert.False(options.Enabled);
        Assert.Equal("branch-test", options.WorkerIdPrefix);
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is PrintSpoolPollingService);
    }

    [Fact]
    public async Task Disabled_service_starts_without_processor_and_does_no_work()
    {
        var service = Service(
            new PrintSpoolPollingOptions { Enabled = false },
            []);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Enabled_service_fails_closed_without_exactly_one_processor()
    {
        var options = EnabledOptions("PRINTER-01");
        var missing = Service(options, []);
        var duplicate = Service(options, [new RecordingProcessor(1), new RecordingProcessor(1)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => missing.StartAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => duplicate.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Separate_printer_lanes_run_concurrently_but_each_lane_is_serial()
    {
        var processor = new RecordingProcessor(expectedConcurrentCalls: 2);
        var service = Service(
            EnabledOptions("PRINTER-01", "PRINTER-02"),
            [processor]);

        await service.StartAsync(CancellationToken.None);
        await processor.WaitUntilExpectedCallsAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            ["PRINTER-01", "PRINTER-02"],
            processor.PrinterIds.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, processor.MaximumGlobalConcurrency);
        Assert.All(processor.MaximumConcurrencyByPrinter.Values, value => Assert.Equal(1, value));
        Assert.All(processor.WorkerIds, value => Assert.Equal("WORKER-TEST", value));
        Assert.All(processor.LeaseDurations, value => Assert.Equal(TimeSpan.FromSeconds(30), value));

        processor.Release();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Lane_retries_after_processor_error_without_stopping_service()
    {
        var processor = new FailsOnceProcessor();
        var options = new PrintSpoolPollingOptions
        {
            Enabled = true,
            PrinterIds = ["PRINTER-01"],
            WorkerIdPrefix = "branch-test",
            LeaseDuration = TimeSpan.FromSeconds(30),
            IdleDelay = TimeSpan.FromHours(1),
            BlockedDelay = TimeSpan.FromHours(1),
            ErrorDelay = TimeSpan.FromMilliseconds(1)
        };
        var service = Service(options, [processor]);

        await service.StartAsync(CancellationToken.None);
        await processor.WaitForSuccessAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(processor.CallCount >= 2);
    }

    [Fact]
    public void Validator_requires_complete_safe_configuration_only_when_enabled()
    {
        var validator = new PrintSpoolPollingOptionsValidator();

        var disabled = validator.Validate(null, new PrintSpoolPollingOptions
        {
            Enabled = false,
            PrinterIds = [],
            WorkerIdPrefix = string.Empty,
            LeaseDuration = TimeSpan.Zero
        });
        var invalid = validator.Validate(null, new PrintSpoolPollingOptions
        {
            Enabled = true,
            PrinterIds = [" PRINTER-01 ", "PRINTER-01"],
            WorkerIdPrefix = " ",
            LeaseDuration = TimeSpan.Zero,
            IdleDelay = TimeSpan.Zero,
            BlockedDelay = TimeSpan.Zero,
            ErrorDelay = TimeSpan.Zero
        });

        Assert.True(disabled.Succeeded);
        Assert.True(invalid.Failed);
        Assert.Contains(invalid.Failures, value => value.Contains("unique", StringComparison.Ordinal));
        Assert.Contains(invalid.Failures, value => value.Contains("prefix", StringComparison.Ordinal));
        Assert.Contains(invalid.Failures, value => value.Contains("LeaseDuration", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_worker_identity_is_unique_per_service_instance()
    {
        var options = Options.Create(new PrintSpoolPollingOptions
        {
            WorkerIdPrefix = "branch-test"
        });

        var first = new RuntimePrintWorkerIdentity(options);
        var second = new RuntimePrintWorkerIdentity(options);

        Assert.StartsWith("branch-test-", first.WorkerId, StringComparison.Ordinal);
        Assert.StartsWith("branch-test-", second.WorkerId, StringComparison.Ordinal);
        Assert.NotEqual(first.WorkerId, second.WorkerId);
    }

    private static PrintSpoolPollingOptions EnabledOptions(params string[] printerIds) =>
        new()
        {
            Enabled = true,
            PrinterIds = printerIds,
            WorkerIdPrefix = "branch-test",
            LeaseDuration = TimeSpan.FromSeconds(30),
            IdleDelay = TimeSpan.FromHours(1),
            BlockedDelay = TimeSpan.FromHours(1),
            ErrorDelay = TimeSpan.FromHours(1)
        };

    private static PrintSpoolPollingService Service(
        PrintSpoolPollingOptions options,
        IEnumerable<IPrintSpoolCycleProcessor> processors) =>
        new(
            Options.Create(options),
            processors,
            new FixedWorkerIdentity(),
            TimeProvider.System,
            NullLogger<PrintSpoolPollingService>.Instance);

    private sealed class FixedWorkerIdentity : IPrintWorkerIdentity
    {
        public string WorkerId => "WORKER-TEST";
    }

    private sealed class RecordingProcessor(int expectedConcurrentCalls) : IPrintSpoolCycleProcessor
    {
        private readonly TaskCompletionSource _expectedCalls =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _sync = new();
        private readonly Dictionary<string, int> _activeByPrinter = new(StringComparer.Ordinal);
        private int _activeGlobal;

        public List<string> PrinterIds { get; } = [];

        public List<string> WorkerIds { get; } = [];

        public List<TimeSpan> LeaseDurations { get; } = [];

        public Dictionary<string, int> MaximumConcurrencyByPrinter { get; } =
            new(StringComparer.Ordinal);

        public int MaximumGlobalConcurrency { get; private set; }

        public async Task<PrintPollingCycleResult> ProcessNextAsync(
            string printerId,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                PrinterIds.Add(printerId);
                WorkerIds.Add(workerId);
                LeaseDurations.Add(leaseDuration);
                _activeGlobal++;
                MaximumGlobalConcurrency = Math.Max(MaximumGlobalConcurrency, _activeGlobal);
                _activeByPrinter.TryGetValue(printerId, out var activeForPrinter);
                activeForPrinter++;
                _activeByPrinter[printerId] = activeForPrinter;
                MaximumConcurrencyByPrinter[printerId] = Math.Max(
                    MaximumConcurrencyByPrinter.GetValueOrDefault(printerId),
                    activeForPrinter);
                if (PrinterIds.Count >= expectedConcurrentCalls)
                {
                    _expectedCalls.TrySetResult();
                }
            }

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return new PrintPollingCycleResult(PrintPollingCycleDisposition.NoWork);
            }
            finally
            {
                lock (_sync)
                {
                    _activeGlobal--;
                    _activeByPrinter[printerId]--;
                }
            }
        }

        public Task WaitUntilExpectedCallsAsync(TimeSpan timeout) =>
            _expectedCalls.Task.WaitAsync(timeout);

        public void Release() => _release.TrySetResult();
    }

    private sealed class FailsOnceProcessor : IPrintSpoolCycleProcessor
    {
        private readonly TaskCompletionSource _success =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<PrintPollingCycleResult> ProcessNextAsync(
            string printerId,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                throw new IOException("simulated polling failure");
            }

            _success.TrySetResult();
            return Task.FromResult(
                new PrintPollingCycleResult(PrintPollingCycleDisposition.NoWork));
        }

        public Task WaitForSuccessAsync(TimeSpan timeout) => _success.Task.WaitAsync(timeout);
    }
}
