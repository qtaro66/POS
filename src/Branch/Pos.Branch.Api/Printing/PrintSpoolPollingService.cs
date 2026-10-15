using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public interface IPrintWorkerIdentity
{
    string WorkerId { get; }
}

public sealed class RuntimePrintWorkerIdentity : IPrintWorkerIdentity
{
    public RuntimePrintWorkerIdentity(IOptions<PrintSpoolPollingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var prefix = string.IsNullOrWhiteSpace(options.Value.WorkerIdPrefix)
            ? "branch-print"
            : options.Value.WorkerIdPrefix.Trim();
        WorkerId = $"{prefix}-{Environment.ProcessId}-{Guid.NewGuid():N}";
    }

    public string WorkerId { get; }
}

public sealed class PrintSpoolPollingService : BackgroundService
{
    private readonly PrintSpoolPollingOptions _options;
    private readonly IReadOnlyList<IPrintSpoolCycleProcessor> _processors;
    private readonly IPrintWorkerIdentity _identity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrintSpoolPollingService> _logger;

    public PrintSpoolPollingService(
        IOptions<PrintSpoolPollingOptions> options,
        IEnumerable<IPrintSpoolCycleProcessor> processors,
        IPrintWorkerIdentity identity,
        TimeProvider timeProvider,
        ILogger<PrintSpoolPollingService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(processors);
        _options = options.Value;
        _processors = processors.ToArray();
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Enabled && _processors.Count != 1)
        {
            return Task.FromException(new InvalidOperationException(
                "Exactly one print spool cycle processor must be registered when polling is enabled."));
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Print spool polling is disabled.");
            return;
        }

        var processor = _processors[0];
        var lanes = _options.PrinterIds
            .Select(printerId => printerId.Trim())
            .Select(printerId => RunPrinterLaneAsync(printerId, processor, stoppingToken));
        await Task.WhenAll(lanes);
    }

    private async Task RunPrinterLaneAsync(
        string printerId,
        IPrintSpoolCycleProcessor processor,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Print spool polling lane started for {PrinterId}.", printerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var result = await processor.ProcessNextAsync(
                    printerId,
                    _identity.WorkerId,
                    _options.LeaseDuration,
                    stoppingToken);
                delay = result.Disposition switch
                {
                    PrintPollingCycleDisposition.NoWork => _options.IdleDelay,
                    PrintPollingCycleDisposition.Blocked => _options.BlockedDelay,
                    PrintPollingCycleDisposition.Processed => TimeSpan.Zero,
                    _ => throw new InvalidOperationException("Unsupported polling disposition.")
                };
                if (result.Disposition is PrintPollingCycleDisposition.Blocked)
                {
                    _logger.LogWarning(
                        "Print spool polling is blocked for {PrinterId}: {BlockedReason}.",
                        printerId,
                        result.Detail ?? "Unspecified");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogError(
                    exception,
                    "Print spool polling cycle failed for {PrinterId}.",
                    printerId);
                delay = _options.ErrorDelay;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Print spool polling lane stopped for {PrinterId}.", printerId);
    }
}
