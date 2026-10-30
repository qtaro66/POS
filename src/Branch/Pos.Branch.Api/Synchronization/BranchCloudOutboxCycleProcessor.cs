using Pos.Application.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudOutboxCycleProcessor :
    IBranchCloudOutboxCycleProcessor
{
    private readonly IBranchCloudOutboxClaimStore _claimStore;
    private readonly IBranchCloudOutboxLeaseRenewalStore _renewalStore;
    private readonly IBranchCloudOutboxDeliveryClient _deliveryClient;
    private readonly IBranchCloudDeliveryConfirmationStore _confirmationStore;
    private readonly IBranchCloudOutboxDeliveryFailureStore _failureStore;
    private readonly IBranchCloudOutboxRetryDelayPolicy _retryDelayPolicy;
    private readonly TimeSpan _maximumRetryDelay;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _failureIdFactory;
    private readonly Func<TimeSpan, CancellationToken, Task> _heartbeatDelay;

    public BranchCloudOutboxCycleProcessor(
        IBranchCloudOutboxClaimStore claimStore,
        IBranchCloudOutboxLeaseRenewalStore renewalStore,
        IBranchCloudOutboxDeliveryClient deliveryClient,
        IBranchCloudDeliveryConfirmationStore confirmationStore,
        IBranchCloudOutboxDeliveryFailureStore failureStore,
        IBranchCloudOutboxRetryDelayPolicy retryDelayPolicy,
        TimeSpan maximumRetryDelay,
        TimeProvider timeProvider,
        Func<string>? failureIdFactory = null,
        Func<TimeSpan, CancellationToken, Task>? heartbeatDelay = null)
    {
        _claimStore = claimStore ?? throw new ArgumentNullException(nameof(claimStore));
        _renewalStore = renewalStore ?? throw new ArgumentNullException(nameof(renewalStore));
        _deliveryClient = deliveryClient ?? throw new ArgumentNullException(nameof(deliveryClient));
        _confirmationStore = confirmationStore
            ?? throw new ArgumentNullException(nameof(confirmationStore));
        _failureStore = failureStore ?? throw new ArgumentNullException(nameof(failureStore));
        _retryDelayPolicy = retryDelayPolicy
            ?? throw new ArgumentNullException(nameof(retryDelayPolicy));
        _maximumRetryDelay = maximumRetryDelay > TimeSpan.Zero
            ? maximumRetryDelay
            : throw new ArgumentOutOfRangeException(nameof(maximumRetryDelay));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _failureIdFactory = failureIdFactory ?? (() => $"CSF-{Guid.NewGuid():N}");
        _heartbeatDelay = heartbeatDelay ??
            ((delay, cancellationToken) =>
                Task.Delay(delay, _timeProvider, cancellationToken));
    }

    public async Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
        string branchId,
        string leaseOwner,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        if (retryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        if (retryDelay > _maximumRetryDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryDelay),
                "The initial retry delay must not exceed the maximum retry delay.");
        }

        var lease = await _claimStore.ClaimNextAsync(
            branchId,
            leaseOwner,
            _timeProvider.GetUtcNow(),
            leaseDuration,
            cancellationToken);
        if (lease is null)
        {
            return new(BranchCloudOutboxCycleDisposition.NoWork);
        }

        await using var heartbeat = new CloudOutboxLeaseHeartbeat(
            _renewalStore,
            lease,
            leaseDuration,
            _timeProvider,
            _heartbeatDelay,
            cancellationToken);

        BranchCloudOutboxDeliveryResult delivery;
        try
        {
            delivery = await _deliveryClient.DeliverAsync(
                    BranchCloudOutboxEventEnvelope.FromLease(lease),
                    heartbeat.OperationToken)
                ?? throw new InvalidOperationException(
                    "The Cloud delivery client returned no result.");
        }
        catch (Exception exception)
            when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            lease = await heartbeat.StopAsync();
            await RecordFailureAsync(
                lease,
                $"DeliveryClientException:{exception.GetType().Name}",
                retryDelay,
                suggestedRetryDelay: null,
                expectedBranchSequence: null,
                cancellationToken);
            return new(BranchCloudOutboxCycleDisposition.Processed);
        }

        lease = await heartbeat.StopAsync();
        switch (delivery.Disposition)
        {
            case BranchCloudOutboxDeliveryDisposition.DurablyAcknowledged:
                if (!string.Equals(
                        delivery.AcknowledgedEventId,
                        lease.OutboxId,
                        StringComparison.Ordinal) ||
                    delivery.AcknowledgedBranchSequence != lease.BranchSequence)
                {
                    await RecordFailureAsync(
                        lease,
                        "AcknowledgmentIdentityMismatch",
                        retryDelay,
                        suggestedRetryDelay: null,
                        expectedBranchSequence: null,
                        cancellationToken);
                    break;
                }

                var confirmation = await _confirmationStore.ConfirmAsync(
                    new BranchCloudDeliveryConfirmation(
                        lease.BranchId,
                        lease.OutboxId,
                        _timeProvider.GetUtcNow()),
                    cancellationToken);
                if (confirmation is BranchCloudDeliveryConfirmationResult.NotFound)
                {
                    throw new InvalidOperationException(
                        "The durably acknowledged Cloud outbox event no longer exists.");
                }

                break;

            case BranchCloudOutboxDeliveryDisposition.RetryableFailure:
                await RecordFailureAsync(
                    lease,
                    delivery.ErrorCode!,
                    retryDelay,
                    delivery.SuggestedRetryDelay,
                    delivery.ExpectedBranchSequence,
                    cancellationToken);
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported Cloud outbox delivery disposition.");
        }

        return new(BranchCloudOutboxCycleDisposition.Processed);
    }

    private async Task RecordFailureAsync(
        BranchCloudOutboxLease lease,
        string errorCode,
        TimeSpan retryDelay,
        TimeSpan? suggestedRetryDelay,
        long? expectedBranchSequence,
        CancellationToken cancellationToken)
    {
        var failedAt = _timeProvider.GetUtcNow();
        var scheduledDelay = _retryDelayPolicy.Calculate(
            retryDelay,
            _maximumRetryDelay,
            lease.RetryCount);
        if (suggestedRetryDelay is { } hint && hint > scheduledDelay)
        {
            scheduledDelay = hint > _maximumRetryDelay
                ? _maximumRetryDelay
                : hint;
        }
        await _failureStore.RecordAsync(
            new BranchCloudOutboxDeliveryFailure(
                RequireFailureId(_failureIdFactory()),
                lease,
                failedAt,
                failedAt.Add(scheduledDelay),
                errorCode,
                expectedBranchSequence),
            cancellationToken);
    }

    private static string RequireFailureId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "The Cloud delivery failure identifier factory returned an empty value.")
            : value.Trim();

    private sealed class CloudOutboxLeaseHeartbeat : IAsyncDisposable
    {
        private readonly IBranchCloudOutboxLeaseRenewalStore _renewalStore;
        private readonly TimeSpan _leaseDuration;
        private readonly TimeSpan _heartbeatInterval;
        private readonly TimeProvider _timeProvider;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly CancellationTokenSource _operationCancellation;
        private readonly CancellationTokenSource _heartbeatCancellation;
        private readonly Task _heartbeatTask;
        private BranchCloudOutboxLease _lease;
        private Exception? _failure;

        public CloudOutboxLeaseHeartbeat(
            IBranchCloudOutboxLeaseRenewalStore renewalStore,
            BranchCloudOutboxLease lease,
            TimeSpan leaseDuration,
            TimeProvider timeProvider,
            Func<TimeSpan, CancellationToken, Task> delay,
            CancellationToken cancellationToken)
        {
            _renewalStore = renewalStore;
            _lease = lease;
            _leaseDuration = leaseDuration;
            _heartbeatInterval = TimeSpan.FromTicks(
                Math.Max(1, leaseDuration.Ticks / 3));
            _timeProvider = timeProvider;
            _delay = delay;
            _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _operationCancellation.Token);
            _heartbeatTask = RunAsync();
        }

        public CancellationToken OperationToken => _operationCancellation.Token;

        public async Task<BranchCloudOutboxLease> StopAsync()
        {
            await _heartbeatCancellation.CancelAsync();
            await _heartbeatTask;
            if (_failure is not null)
            {
                throw new InvalidOperationException(
                    "The Cloud outbox lease heartbeat could not renew ownership.",
                    _failure);
            }

            return _lease;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                _heartbeatCancellation.Dispose();
                _operationCancellation.Dispose();
            }
        }

        private async Task RunAsync()
        {
            try
            {
                while (true)
                {
                    await _delay(_heartbeatInterval, _heartbeatCancellation.Token);
                    _lease = await _renewalStore.RenewAsync(
                        _lease,
                        _timeProvider.GetUtcNow(),
                        _leaseDuration,
                        _heartbeatCancellation.Token);
                }
            }
            catch (OperationCanceledException)
                when (_heartbeatCancellation.IsCancellationRequested)
            {
                // Normal shutdown after delivery ends or the caller cancels.
            }
            catch (Exception exception)
            {
                _failure = exception;
                await _operationCancellation.CancelAsync();
            }
        }
    }
}
