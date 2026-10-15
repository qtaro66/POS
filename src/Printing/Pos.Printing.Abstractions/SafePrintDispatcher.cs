namespace Pos.Printing.Abstractions;

public sealed class SafePrintDispatcher : IPrintDispatcher
{
    private readonly IPrinterConfigurationProvider _configurationProvider;
    private readonly IPrinterConditionProbe _conditionProbe;
    private readonly IReadOnlyDictionary<PrinterProtocol, IPrintTransportAdapter> _adapters;

    public SafePrintDispatcher(
        IPrinterConfigurationProvider configurationProvider,
        IPrinterConditionProbe conditionProbe,
        IEnumerable<IPrintTransportAdapter> adapters)
    {
        _configurationProvider = configurationProvider
            ?? throw new ArgumentNullException(nameof(configurationProvider));
        _conditionProbe = conditionProbe ?? throw new ArgumentNullException(nameof(conditionProbe));
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToDictionary(adapter => adapter.Protocol);
    }

    public async Task<PrintDispatchResult> DispatchAsync(
        PrintDispatchRequest request,
        IPrintDispatchLifecycle? lifecycle = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);
        var printJobId = Require(request.PrintJobId, nameof(request.PrintJobId));
        var branchId = Require(request.BranchId, nameof(request.BranchId));
        var stationId = Require(
            request.ProductionStationId,
            nameof(request.ProductionStationId));
        request = request with
        {
            PrintJobId = printJobId,
            BranchId = branchId,
            ProductionStationId = stationId
        };
        var profile = await _configurationProvider.FindActivePrinterAsync(
            branchId,
            stationId,
            cancellationToken);
        if (profile is null)
        {
            return Blocked(request, null, "NoActivePrinter");
        }

        if (!string.Equals(profile.BranchId, branchId, StringComparison.Ordinal) ||
            !string.Equals(
                profile.ProductionStationId,
                stationId,
                StringComparison.Ordinal))
        {
            return Blocked(request, profile.PrinterId, "PrinterConfigurationMismatch");
        }

        PrinterCondition condition;
        try
        {
            condition = await _conditionProbe.GetConditionAsync(profile, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            return Blocked(
                request,
                profile.PrinterId,
                $"ConditionProbeFailed:{exception.GetType().Name}");
        }
        if (condition is not (PrinterCondition.Ready or PrinterCondition.StatusUnsupported))
        {
            return Blocked(request, profile.PrinterId, condition.ToString());
        }

        if (!_adapters.TryGetValue(profile.Protocol, out var adapter))
        {
            return Blocked(request, profile.PrinterId, "ProtocolAdapterUnavailable");
        }

        var observer = new SendObserver(request, profile, lifecycle);
        if (lifecycle is not null)
        {
            await lifecycle.OnTransportAttemptStartingAsync(
                request,
                profile,
                cancellationToken);
        }

        try
        {
            await adapter.SendAsync(profile, request.Payload, observer, cancellationToken);
            if (!observer.AttemptStarted)
            {
                return new PrintDispatchResult(
                    printJobId,
                    profile.PrinterId,
                    PrintDispatchOutcome.Unknown,
                    false,
                    false,
                    null,
                    "TransportStartNotObserved");
            }

            return new PrintDispatchResult(
                printJobId,
                profile.PrinterId,
                PrintDispatchOutcome.PrintDispatched,
                true,
                false,
                null,
                null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new PrintDispatchResult(
                printJobId,
                profile.PrinterId,
                observer.AttemptStarted
                    ? PrintDispatchOutcome.Unknown
                    : PrintDispatchOutcome.Failed,
                observer.AttemptStarted,
                !observer.AttemptStarted &&
                    profile.MaximumAutomaticRetriesBeforeSend > 0,
                null,
                exception.GetType().Name);
        }
    }

    private static PrintDispatchResult Blocked(
        PrintDispatchRequest request,
        string? printerId,
        string reason) =>
        new(
            request.PrintJobId,
            printerId,
            PrintDispatchOutcome.Blocked,
            false,
            false,
            reason,
            null);

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();

    private sealed class SendObserver(
        PrintDispatchRequest request,
        PrinterProfile profile,
        IPrintDispatchLifecycle? lifecycle) : IPrintSendObserver
    {
        private int _attemptStarted;
        private readonly Lock _sync = new();
        private Task? _firstByteTask;

        public bool AttemptStarted => Volatile.Read(ref _attemptStarted) == 1;

        public Task OnFirstByteSendingAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return _firstByteTask ??= MarkFirstByteSendingAsync(cancellationToken);
            }
        }

        private async Task MarkFirstByteSendingAsync(CancellationToken cancellationToken)
        {
            // Crossing this boundary authorizes the adapter to send. If the durable
            // journal fails ambiguously, the result must remain Unknown, never retryable.
            Volatile.Write(ref _attemptStarted, 1);
            if (lifecycle is not null)
            {
                await lifecycle.OnFirstByteSendingAsync(
                    request,
                    profile,
                    cancellationToken);
            }
        }
    }
}
