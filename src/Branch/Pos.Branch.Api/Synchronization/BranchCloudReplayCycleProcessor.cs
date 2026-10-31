using Pos.Application.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudReplayCycleProcessor :
    IBranchCloudReplayCycleProcessor
{
    private readonly IBranchCloudOutboxSequenceReplayClaimStore _claimStore;
    private readonly IBranchCloudOutboxSequenceReplayLeaseRenewalStore _renewalStore;
    private readonly IBranchCloudOutboxDeliveryClient _deliveryClient;
    private readonly IBranchCloudOutboxSequenceReplayCompletionStore _confirmationStore;
    private readonly IBranchCloudOutboxSequenceReplayFailureStore _failureStore;
    private readonly IBranchCloudOutboxSequenceReplayStore _replayStore;
    private readonly IBranchCloudOutboxRetryDelayPolicy _retryDelayPolicy;
    private readonly TimeSpan _maximumRetryDelay;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _failureIdFactory;
    private readonly Func<TimeSpan, CancellationToken, Task> _heartbeatDelay;

    public BranchCloudReplayCycleProcessor(
        IBranchCloudOutboxSequenceReplayClaimStore claimStore,
        IBranchCloudOutboxSequenceReplayLeaseRenewalStore renewalStore,
        IBranchCloudOutboxDeliveryClient deliveryClient,
        IBranchCloudOutboxSequenceReplayCompletionStore confirmationStore,
        IBranchCloudOutboxSequenceReplayFailureStore failureStore,
        IBranchCloudOutboxSequenceReplayStore replayStore,
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
        _replayStore = replayStore ?? throw new ArgumentNullException(nameof(replayStore));
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

        await using var heartbeat = new BranchCloudReplayLeaseHeartbeat(
            _renewalStore,
            lease,
            leaseDuration,
            _timeProvider,
            cancellationToken,
            _heartbeatDelay);

        BranchCloudOutboxDeliveryResult delivery;
        try
        {
            delivery = await _deliveryClient.DeliverAsync(
                    BranchCloudOutboxEventEnvelope.FromReplayLease(lease),
                    heartbeat.OperationToken)
                ?? throw new InvalidOperationException(
                    "The Cloud delivery client returned no result.");
        }
        catch (Exception exception)
            when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            lease = await heartbeat.StopAsync();
            cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
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

                var confirmation = await _confirmationStore.CompleteAsync(
                    new BranchCloudOutboxSequenceReplayCompletion(
                        lease,
                        lease.OutboxId,
                        lease.BranchSequence,
                        _timeProvider.GetUtcNow()),
                    cancellationToken);
                if (confirmation is BranchCloudOutboxSequenceReplayCompletionResult.NotFound)
                {
                    throw new InvalidOperationException(
                        "The durably acknowledged Cloud outbox event no longer exists.");
                }

                break;

            case BranchCloudOutboxDeliveryDisposition.RetryableFailure:
                var failure = await RecordFailureAsync(
                    lease,
                    delivery.ErrorCode!,
                    retryDelay,
                    delivery.SuggestedRetryDelay,
                    delivery.ExpectedBranchSequence,
                    cancellationToken);
                if (failure.ExpectedBranchSequence is { } expectedSequence)
                {
                    var replay = await _replayStore.RequestAsync(
                        new BranchCloudOutboxSequenceReplayRequest(
                            failure.FailureId,
                            failure.Lease.BranchId,
                            expectedSequence,
                            failure.Lease.BranchSequence,
                            failure.FailedAt),
                        cancellationToken);
                    if (replay is BranchCloudOutboxSequenceReplayResult.ExpectedSequenceNotFound)
                    {
                        throw new InvalidOperationException(
                            "The Cloud-requested Branch sequence is no longer retained for replay.");
                    }
                }
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported Cloud outbox delivery disposition.");
        }

        return new(BranchCloudOutboxCycleDisposition.Processed);
    }

    private async Task<BranchCloudOutboxSequenceReplayFailure> RecordFailureAsync(
        BranchCloudOutboxSequenceReplayLease lease,
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
        var failure = new BranchCloudOutboxSequenceReplayFailure(
            RequireFailureId(_failureIdFactory()),
            lease,
            failedAt,
            failedAt.Add(scheduledDelay),
            errorCode,
            expectedBranchSequence);
        await _failureStore.RecordAsync(failure, cancellationToken);
        return failure;
    }

    private static string RequireFailureId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "The Cloud delivery failure identifier factory returned an empty value.")
            : value.Trim();

}
