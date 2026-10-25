using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudOutboxPollingServiceTests
{
    [Fact]
    public async Task Disabled_service_starts_without_processor_and_does_no_work()
    {
        var service = Service(
            new BranchCloudOutboxDeliveryOptions { Enabled = false },
            []);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
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
    public async Task Polling_is_serial_and_forwards_validated_runtime_values()
    {
        var processor = new RecordingProcessor();
        var service = Service(
            EnabledOptions(TimeSpan.FromSeconds(5)),
            [processor]);

        await service.StartAsync(CancellationToken.None);
        await processor.WaitForCallAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, processor.MaximumConcurrency);
        Assert.Equal("BRANCH-01", processor.BranchId);
        Assert.Equal("WORKER-TEST", processor.LeaseOwner);
        Assert.Equal(TimeSpan.FromSeconds(30), processor.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), processor.RetryDelay);

        processor.Release();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Processor_error_is_retried_without_stopping_service()
    {
        var processor = new FailsOnceProcessor();
        var options = EnabledOptions(TimeSpan.FromMilliseconds(1));
        var service = Service(options, [processor]);

        await service.StartAsync(CancellationToken.None);
        await processor.WaitForSuccessAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(processor.CallCount >= 2);
    }

    [Fact]
    public void Runtime_worker_identity_is_unique_per_service_instance()
    {
        var options = Options.Create(new BranchCloudOutboxDeliveryOptions
        {
            WorkerId = "branch-cloud-test"
        });

        var first = new RuntimeBranchCloudOutboxWorkerIdentity(options);
        var second = new RuntimeBranchCloudOutboxWorkerIdentity(options);

        Assert.StartsWith("branch-cloud-test-", first.WorkerId, StringComparison.Ordinal);
        Assert.StartsWith("branch-cloud-test-", second.WorkerId, StringComparison.Ordinal);
        Assert.NotEqual(first.WorkerId, second.WorkerId);
    }

    private static BranchCloudOutboxDeliveryOptions EnabledOptions(
        TimeSpan? retryDelay = null) =>
        new()
        {
            Enabled = true,
            BranchId = "BRANCH-01",
            WorkerId = "branch-cloud-test",
            LeaseDuration = TimeSpan.FromSeconds(30),
            IdleDelay = TimeSpan.FromHours(1),
            RetryDelay = retryDelay ?? TimeSpan.FromHours(1)
        };

    private static BranchCloudOutboxPollingService Service(
        BranchCloudOutboxDeliveryOptions options,
        IEnumerable<IBranchCloudOutboxCycleProcessor> processors) =>
        new(
            Options.Create(options),
            processors,
            new FixedWorkerIdentity(),
            TimeProvider.System,
            NullLogger<BranchCloudOutboxPollingService>.Instance);

    private sealed class FixedWorkerIdentity : IBranchCloudOutboxWorkerIdentity
    {
        public string WorkerId => "WORKER-TEST";
    }

    private sealed class RecordingProcessor : IBranchCloudOutboxCycleProcessor
    {
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;

        public int MaximumConcurrency { get; private set; }

        public string? BranchId { get; private set; }

        public string? LeaseOwner { get; private set; }

        public TimeSpan LeaseDuration { get; private set; }

        public TimeSpan RetryDelay { get; private set; }

        public async Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
            string branchId,
            string leaseOwner,
            TimeSpan leaseDuration,
            TimeSpan retryDelay,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            BranchId = branchId;
            LeaseOwner = leaseOwner;
            LeaseDuration = leaseDuration;
            RetryDelay = retryDelay;
            _called.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return new(BranchCloudOutboxCycleDisposition.NoWork);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task WaitForCallAsync(TimeSpan timeout) => _called.Task.WaitAsync(timeout);

        public void Release() => _release.TrySetResult();
    }

    private sealed class FailsOnceProcessor : IBranchCloudOutboxCycleProcessor
    {
        private readonly TaskCompletionSource _success =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
            string branchId,
            string leaseOwner,
            TimeSpan leaseDuration,
            TimeSpan retryDelay,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                throw new IOException("simulated Cloud polling failure");
            }

            _success.TrySetResult();
            return Task.FromResult(
                new BranchCloudOutboxCycleResult(
                    BranchCloudOutboxCycleDisposition.NoWork));
        }

        public Task WaitForSuccessAsync(TimeSpan timeout) => _success.Task.WaitAsync(timeout);
    }
}
