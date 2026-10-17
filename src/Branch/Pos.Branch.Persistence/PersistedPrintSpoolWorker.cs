using Pos.Printing.Abstractions;

namespace Pos.Branch.Infrastructure.Sqlite;

public enum PrintSpoolCycleOutcome
{
    NoWork,
    Blocked,
    Failed,
    PrintDispatched,
    Unknown
}

public sealed record PrintSpoolCycleResult(
    PrintSpoolCycleOutcome Outcome,
    string? PrintJobId,
    string? PrintAttemptId,
    string? Detail);

public sealed class PersistedPrintSpoolWorker
{
    private readonly SqlitePrintDispatchStore _dispatchStore;
    private readonly SqliteRenderedPrintPayloadStore _payloadStore;
    private readonly IPrinterConfigurationProvider _configurationProvider;
    private readonly IPrintPayloadRenderer _renderer;
    private readonly IPrintDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _attemptIdFactory;
    private readonly Func<TimeSpan, CancellationToken, Task> _leaseHeartbeatDelay;

    public PersistedPrintSpoolWorker(
        SqlitePrintDispatchStore dispatchStore,
        SqliteRenderedPrintPayloadStore payloadStore,
        IPrinterConfigurationProvider configurationProvider,
        IPrintPayloadRenderer renderer,
        IPrintDispatcher dispatcher,
        TimeProvider? timeProvider = null,
        Func<string>? attemptIdFactory = null,
        Func<TimeSpan, CancellationToken, Task>? leaseHeartbeatDelay = null)
    {
        _dispatchStore = dispatchStore ?? throw new ArgumentNullException(nameof(dispatchStore));
        _payloadStore = payloadStore ?? throw new ArgumentNullException(nameof(payloadStore));
        _configurationProvider = configurationProvider
            ?? throw new ArgumentNullException(nameof(configurationProvider));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _attemptIdFactory = attemptIdFactory
            ?? (() => $"PAT-{Guid.NewGuid():N}");
        _leaseHeartbeatDelay = leaseHeartbeatDelay
            ?? ((delay, cancellationToken) =>
                Task.Delay(delay, _timeProvider, cancellationToken));
    }

    public async Task<PrintSpoolCycleResult> ProcessNextAsync(
        string printerId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var lease = await _dispatchStore.ClaimNextAsync(
            printerId,
            workerId,
            _timeProvider.GetUtcNow(),
            leaseDuration,
            cancellationToken);
        if (lease is null)
        {
            return new PrintSpoolCycleResult(PrintSpoolCycleOutcome.NoWork, null, null, null);
        }

        await using var heartbeat = new PrintLeaseHeartbeat(
            _dispatchStore,
            lease,
            leaseDuration,
            _timeProvider,
            _leaseHeartbeatDelay,
            cancellationToken);
        var operationToken = heartbeat.OperationToken;

        var profile = await _configurationProvider.FindActivePrinterAsync(
            lease.BranchId,
            lease.ProductionStationId,
            operationToken);
        if (profile is null ||
            !string.Equals(profile.PrinterId, lease.PrinterId, StringComparison.Ordinal))
        {
            const string reason = "LeasedPrinterConfigurationUnavailable";
            lease = await heartbeat.StopAsync();
            await _dispatchStore.ReleaseBlockedAsync(lease, reason, cancellationToken);
            return new PrintSpoolCycleResult(
                PrintSpoolCycleOutcome.Blocked,
                lease.PrintJobId,
                null,
                reason);
        }

        var payload = await _payloadStore.FindAsync(
            lease,
            _timeProvider.GetUtcNow(),
            operationToken);
        if (payload is null)
        {
            try
            {
                var document = await _payloadStore.ReadSourceDocumentAsync(
                    lease,
                    _timeProvider.GetUtcNow(),
                    operationToken);
                payload = await _renderer.RenderAsync(
                    new PrintRenderRequest(document, profile),
                    operationToken);
                payload = await _payloadStore.SaveAsync(
                    lease,
                    payload,
                    _timeProvider.GetUtcNow(),
                    operationToken);
            }
            catch (Exception exception)
                when (exception is not (OperationCanceledException or OutOfMemoryException))
            {
                var reason = $"PayloadPreparationFailed:{exception.GetType().Name}";
                lease = await heartbeat.StopAsync();
                await _dispatchStore.ReleaseBlockedAsync(lease, reason, cancellationToken);
                return new PrintSpoolCycleResult(
                    PrintSpoolCycleOutcome.Blocked,
                    lease.PrintJobId,
                    null,
                    reason);
            }
        }

        var request = new PrintDispatchRequest(
            lease.PrintJobId,
            lease.BranchId,
            lease.ProductionStationId,
            payload);
        var attemptId = RequireAttemptId(_attemptIdFactory());
        var lifecycle = new SqlitePrintDispatchLifecycle(
            _dispatchStore,
            lease,
            attemptId,
            _timeProvider);
        var result = await _dispatcher.DispatchAsync(request, lifecycle, operationToken);
        lease = await heartbeat.StopAsync();
        if (result.Outcome is PrintDispatchOutcome.Blocked)
        {
            var reason = result.BlockedReason ?? "DispatchBlocked";
            await _dispatchStore.ReleaseBlockedAsync(lease, reason, cancellationToken);
            return new PrintSpoolCycleResult(
                PrintSpoolCycleOutcome.Blocked,
                lease.PrintJobId,
                null,
                reason);
        }

        await _dispatchStore.CompleteAttemptAsync(
            lease,
            attemptId,
            result,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        return new PrintSpoolCycleResult(
            result.Outcome switch
            {
                PrintDispatchOutcome.Failed => PrintSpoolCycleOutcome.Failed,
                PrintDispatchOutcome.PrintDispatched => PrintSpoolCycleOutcome.PrintDispatched,
                PrintDispatchOutcome.Unknown => PrintSpoolCycleOutcome.Unknown,
                _ => throw new InvalidOperationException("Unsupported dispatch outcome.")
            },
            lease.PrintJobId,
            attemptId,
            result.ErrorCode);
    }

    private static string RequireAttemptId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("The print-attempt identifier factory returned an empty value.")
            : value.Trim();

    private sealed class PrintLeaseHeartbeat : IAsyncDisposable
    {
        private readonly SqlitePrintDispatchStore _dispatchStore;
        private readonly TimeSpan _leaseDuration;
        private readonly TimeSpan _heartbeatInterval;
        private readonly TimeProvider _timeProvider;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly CancellationTokenSource _operationCancellation;
        private readonly CancellationTokenSource _heartbeatCancellation;
        private readonly Task _heartbeatTask;
        private PrintJobLease _lease;
        private Exception? _failure;

        public PrintLeaseHeartbeat(
            SqlitePrintDispatchStore dispatchStore,
            PrintJobLease lease,
            TimeSpan leaseDuration,
            TimeProvider timeProvider,
            Func<TimeSpan, CancellationToken, Task> delay,
            CancellationToken cancellationToken)
        {
            _dispatchStore = dispatchStore;
            _lease = lease;
            _leaseDuration = leaseDuration;
            _heartbeatInterval = TimeSpan.FromTicks(Math.Max(1, leaseDuration.Ticks / 3));
            _timeProvider = timeProvider;
            _delay = delay;
            _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _operationCancellation.Token);
            _heartbeatTask = RunAsync();
        }

        public CancellationToken OperationToken => _operationCancellation.Token;

        public async Task<PrintJobLease> StopAsync()
        {
            await _heartbeatCancellation.CancelAsync();
            await _heartbeatTask;
            if (_failure is not null)
            {
                throw new InvalidOperationException(
                    "The print-job lease heartbeat could not renew ownership.",
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
                    _lease = await _dispatchStore.RenewLeaseAsync(
                        _lease,
                        _timeProvider.GetUtcNow(),
                        _leaseDuration,
                        _heartbeatCancellation.Token);
                }
            }
            catch (OperationCanceledException)
                when (_heartbeatCancellation.IsCancellationRequested)
            {
                // Normal shutdown after the leased work ends or the caller cancels.
            }
            catch (Exception exception)
            {
                _failure = exception;
                await _operationCancellation.CancelAsync();
            }
        }
    }
}
