using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudOutboxCycleProcessorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 25, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_claim_returns_no_work_without_delivery()
    {
        var stores = new RecordingStores { Lease = null };
        var client = new RecordingClient(BranchCloudOutboxDeliveryResult.Acknowledged());
        var processor = Processor(stores, client);

        var result = await processor.ProcessNextAsync(
            "BRANCH-01",
            "WORKER-01",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5));

        Assert.Equal(BranchCloudOutboxCycleDisposition.NoWork, result.Disposition);
        Assert.Equal(0, client.CallCount);
        Assert.Null(stores.Confirmation);
        Assert.Null(stores.Failure);
    }

    [Theory]
    [InlineData(BranchCloudDeliveryConfirmationResult.Confirmed)]
    [InlineData(BranchCloudDeliveryConfirmationResult.AlreadyConfirmed)]
    public async Task Durable_acknowledgment_is_confirmed(
        BranchCloudDeliveryConfirmationResult confirmationResult)
    {
        var stores = new RecordingStores
        {
            Lease = Lease(),
            ConfirmationResult = confirmationResult
        };
        var client = new RecordingClient(BranchCloudOutboxDeliveryResult.Acknowledged());
        var processor = Processor(stores, client);

        var result = await processor.ProcessNextAsync(
            "BRANCH-01",
            "WORKER-01",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5));

        Assert.Equal(BranchCloudOutboxCycleDisposition.Processed, result.Disposition);
        Assert.Same(stores.Lease, client.Lease);
        Assert.Equal("BRANCH-01", stores.Confirmation?.BranchId);
        Assert.Equal("OUTBOX-01", stores.Confirmation?.OutboxId);
        Assert.Equal(Now, stores.Confirmation?.AcknowledgedAt);
        Assert.Null(stores.Failure);
    }

    [Fact]
    public async Task Retryable_result_records_failure_and_next_retry()
    {
        var stores = new RecordingStores { Lease = Lease() };
        var client = new RecordingClient(
            BranchCloudOutboxDeliveryResult.Failed("CLOUD_UNAVAILABLE"));
        var processor = Processor(stores, client);

        var result = await processor.ProcessNextAsync(
            "BRANCH-01",
            "WORKER-01",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5));

        Assert.Equal(BranchCloudOutboxCycleDisposition.Processed, result.Disposition);
        Assert.Equal("FAILURE-01", stores.Failure?.FailureId);
        Assert.Equal("CLOUD_UNAVAILABLE", stores.Failure?.ErrorCode);
        Assert.Equal(Now, stores.Failure?.FailedAt);
        Assert.Equal(Now.AddSeconds(5), stores.Failure?.NextRetryAt);
        Assert.Null(stores.Confirmation);
    }

    [Fact]
    public async Task Unexpected_client_error_is_persisted_as_retryable_failure()
    {
        var stores = new RecordingStores { Lease = Lease() };
        var client = new RecordingClient(new IOException("simulated transport fault"));
        var processor = Processor(stores, client);

        var result = await processor.ProcessNextAsync(
            "BRANCH-01",
            "WORKER-01",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5));

        Assert.Equal(BranchCloudOutboxCycleDisposition.Processed, result.Disposition);
        Assert.Equal("DeliveryClientException:IOException", stores.Failure?.ErrorCode);
    }

    [Fact]
    public async Task Missing_event_after_durable_acknowledgment_fails_closed()
    {
        var stores = new RecordingStores
        {
            Lease = Lease(),
            ConfirmationResult = BranchCloudDeliveryConfirmationResult.NotFound
        };
        var processor = Processor(
            stores,
            new RecordingClient(BranchCloudOutboxDeliveryResult.Acknowledged()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessNextAsync(
                "BRANCH-01",
                "WORKER-01",
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5)));

        Assert.Contains("no longer exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Slow_delivery_renews_lease_before_confirmation()
    {
        var time = new MutableTimeProvider(Now);
        var delay = new ControlledHeartbeatDelay();
        var stores = new RecordingStores { Lease = Lease() };
        var client = new WaitForRenewalClient(stores);
        var processor = Processor(stores, client, time, delay.DelayAsync);

        var processing = processor.ProcessNextAsync(
            "BRANCH-01",
            "WORKER-01",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5));
        await delay.WaitUntilStartedAsync(TimeSpan.FromSeconds(5));
        time.UtcNow = Now.AddSeconds(10);
        delay.Release();

        var result = await processing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(BranchCloudOutboxCycleDisposition.Processed, result.Disposition);
        Assert.Equal(1, stores.RenewalCount);
        Assert.Equal(Now.AddSeconds(40), stores.ConfirmationLeaseExpiry);
    }

    [Theory]
    [InlineData(0, 1, "leaseDuration")]
    [InlineData(-1, 1, "leaseDuration")]
    [InlineData(1, 0, "retryDelay")]
    [InlineData(1, -1, "retryDelay")]
    public async Task Non_positive_durations_are_rejected(
        int leaseSeconds,
        int retrySeconds,
        string parameterName)
    {
        var processor = Processor(
            new RecordingStores { Lease = Lease() },
            new RecordingClient(BranchCloudOutboxDeliveryResult.Acknowledged()));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            processor.ProcessNextAsync(
                "BRANCH-01",
                "WORKER-01",
                TimeSpan.FromSeconds(leaseSeconds),
                TimeSpan.FromSeconds(retrySeconds)));

        Assert.Equal(parameterName, exception.ParamName);
    }

    private static BranchCloudOutboxCycleProcessor Processor(
        RecordingStores stores,
        IBranchCloudOutboxDeliveryClient client,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? heartbeatDelay = null) =>
        new(
            stores,
            stores,
            client,
            stores,
            stores,
            timeProvider ?? new MutableTimeProvider(Now),
            () => "FAILURE-01",
            heartbeatDelay);

    private static BranchCloudOutboxLease Lease(
        DateTimeOffset? claimedAt = null,
        DateTimeOffset? leaseExpiresAt = null) =>
        new(
            "OUTBOX-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            TableSessionId.From("SESSION-01"),
            TableSessionAction.SubmitOrder,
            3,
            Now.AddMinutes(-1),
            0,
            "WORKER-01",
            claimedAt ?? Now,
            leaseExpiresAt ?? Now.AddSeconds(30));

    private sealed class RecordingStores :
        IBranchCloudOutboxClaimStore,
        IBranchCloudOutboxLeaseRenewalStore,
        IBranchCloudDeliveryConfirmationStore,
        IBranchCloudOutboxDeliveryFailureStore
    {
        private readonly TaskCompletionSource _renewed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BranchCloudOutboxLease? Lease { get; set; }

        public int RenewalCount { get; private set; }

        public BranchCloudDeliveryConfirmation? Confirmation { get; private set; }

        public BranchCloudOutboxDeliveryFailure? Failure { get; private set; }

        public DateTimeOffset? ConfirmationLeaseExpiry { get; private set; }

        public BranchCloudDeliveryConfirmationResult ConfirmationResult { get; set; } =
            BranchCloudDeliveryConfirmationResult.Confirmed;

        public Task<BranchCloudOutboxLease?> ClaimNextAsync(
            string branchId,
            string leaseOwner,
            DateTimeOffset claimedAt,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Lease);

        public Task<BranchCloudOutboxLease> RenewAsync(
            BranchCloudOutboxLease lease,
            DateTimeOffset renewedAt,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            RenewalCount++;
            Lease = new BranchCloudOutboxLease(
                lease.OutboxId,
                lease.BranchId,
                lease.BranchSequence,
                lease.RequestId,
                lease.TableSessionId,
                lease.Action,
                lease.TableVersion,
                lease.CreatedAt,
                lease.RetryCount,
                lease.LeaseOwner,
                lease.ClaimedAt,
                renewedAt.Add(leaseDuration));
            _renewed.TrySetResult();
            return Task.FromResult(Lease);
        }

        public Task<BranchCloudDeliveryConfirmationResult> ConfirmAsync(
            BranchCloudDeliveryConfirmation confirmation,
            CancellationToken cancellationToken = default)
        {
            Confirmation = confirmation;
            ConfirmationLeaseExpiry = Lease?.LeaseExpiresAt;
            return Task.FromResult(ConfirmationResult);
        }

        public Task<BranchCloudOutboxDeliveryFailureResult> RecordAsync(
            BranchCloudOutboxDeliveryFailure failure,
            CancellationToken cancellationToken = default)
        {
            Failure = failure;
            return Task.FromResult(new BranchCloudOutboxDeliveryFailureResult(1, false));
        }

        public Task WaitForRenewalAsync(CancellationToken cancellationToken) =>
            _renewed.Task.WaitAsync(cancellationToken);
    }

    private sealed class RecordingClient : IBranchCloudOutboxDeliveryClient
    {
        private readonly BranchCloudOutboxDeliveryResult? _result;
        private readonly Exception? _exception;

        public RecordingClient(BranchCloudOutboxDeliveryResult result) =>
            _result = result;

        public RecordingClient(Exception exception) =>
            _exception = exception;

        public int CallCount { get; private set; }

        public BranchCloudOutboxLease? Lease { get; private set; }

        public Task<BranchCloudOutboxDeliveryResult> DeliverAsync(
            BranchCloudOutboxLease lease,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Lease = lease;
            return _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<BranchCloudOutboxDeliveryResult>(_exception);
        }
    }

    private sealed class WaitForRenewalClient(RecordingStores stores) :
        IBranchCloudOutboxDeliveryClient
    {
        public async Task<BranchCloudOutboxDeliveryResult> DeliverAsync(
            BranchCloudOutboxLease lease,
            CancellationToken cancellationToken = default)
        {
            await stores.WaitForRenewalAsync(cancellationToken);
            return BranchCloudOutboxDeliveryResult.Acknowledged();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class ControlledHeartbeatDelay
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public async Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
                return;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task WaitUntilStartedAsync(TimeSpan timeout) =>
            _started.Task.WaitAsync(timeout);

        public void Release() => _release.TrySetResult();
    }
}
