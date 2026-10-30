using Pos.Application.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudReplayLeaseHeartbeat : IAsyncDisposable
{
    private readonly IBranchCloudOutboxSequenceReplayLeaseRenewalStore _store;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _operation;
    private readonly CancellationTokenSource _heartbeat;
    private readonly Task _task;
    private BranchCloudOutboxSequenceReplayLease _lease;
    private Exception? _failure;

    public BranchCloudReplayLeaseHeartbeat(
        IBranchCloudOutboxSequenceReplayLeaseRenewalStore store,
        BranchCloudOutboxSequenceReplayLease lease,
        TimeSpan leaseDuration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }
        _duration = leaseDuration;
        _interval = TimeSpan.FromTicks(Math.Max(1, leaseDuration.Ticks / 3));
        _delay = delay ?? ((interval, token) => Task.Delay(interval, _time, token));
        _operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _heartbeat = CancellationTokenSource.CreateLinkedTokenSource(_operation.Token);
        _task = RunAsync();
    }

    public CancellationToken OperationToken => _operation.Token;

    public async Task<BranchCloudOutboxSequenceReplayLease> StopAsync()
    {
        await _heartbeat.CancelAsync();
        await _task;
        if (_failure is not null)
        {
            throw new InvalidOperationException("Replay lease renewal failed; delivery ownership is uncertain.", _failure);
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
            _heartbeat.Dispose();
            _operation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _delay(_interval, _heartbeat.Token);
                _heartbeat.Token.ThrowIfCancellationRequested();
                _lease = await _store.RenewAsync(_lease, _time.GetUtcNow(), _duration, _heartbeat.Token);
            }
        }
        catch (OperationCanceledException) when (_heartbeat.IsCancellationRequested)
        {
            // Caller cancellation or normal heartbeat shutdown.
        }
        catch (Exception exception)
        {
            _failure = exception;
            await _operation.CancelAsync();
        }
    }
}
