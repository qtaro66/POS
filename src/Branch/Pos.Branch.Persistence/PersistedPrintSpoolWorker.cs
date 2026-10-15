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

    public PersistedPrintSpoolWorker(
        SqlitePrintDispatchStore dispatchStore,
        SqliteRenderedPrintPayloadStore payloadStore,
        IPrinterConfigurationProvider configurationProvider,
        IPrintPayloadRenderer renderer,
        IPrintDispatcher dispatcher,
        TimeProvider? timeProvider = null,
        Func<string>? attemptIdFactory = null)
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

        var profile = await _configurationProvider.FindActivePrinterAsync(
            lease.BranchId,
            lease.ProductionStationId,
            cancellationToken);
        if (profile is null ||
            !string.Equals(profile.PrinterId, lease.PrinterId, StringComparison.Ordinal))
        {
            const string reason = "LeasedPrinterConfigurationUnavailable";
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
            cancellationToken);
        if (payload is null)
        {
            try
            {
                var document = await _payloadStore.ReadSourceDocumentAsync(
                    lease,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                payload = await _renderer.RenderAsync(
                    new PrintRenderRequest(document, profile),
                    cancellationToken);
                payload = await _payloadStore.SaveAsync(
                    lease,
                    payload,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            catch (Exception exception)
                when (exception is not (OperationCanceledException or OutOfMemoryException))
            {
                var reason = $"PayloadPreparationFailed:{exception.GetType().Name}";
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
        var result = await _dispatcher.DispatchAsync(request, lifecycle, cancellationToken);
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
}
