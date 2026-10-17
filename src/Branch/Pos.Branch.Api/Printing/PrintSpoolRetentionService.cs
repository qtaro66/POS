using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Printing;

public interface IPrintSpoolRetentionProcessor
{
    Task<RenderedPrintPayloadPruneResult> PruneAsync(
        DateTimeOffset deleteBefore,
        long maximumTotalBytes,
        int maximumDeletes,
        CancellationToken cancellationToken);
}

public sealed class PersistedPrintSpoolRetentionProcessor(
    SqliteRenderedPrintPayloadRetentionStore store) : IPrintSpoolRetentionProcessor
{
    private readonly SqliteRenderedPrintPayloadRetentionStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    public Task<RenderedPrintPayloadPruneResult> PruneAsync(
        DateTimeOffset deleteBefore,
        long maximumTotalBytes,
        int maximumDeletes,
        CancellationToken cancellationToken) =>
        _store.PruneAsync(
            deleteBefore,
            maximumTotalBytes,
            maximumDeletes,
            cancellationToken);
}

public sealed class PrintSpoolRetentionService : BackgroundService
{
    private readonly PrintSpoolRetentionOptions _options;
    private readonly IReadOnlyList<IPrintSpoolRetentionProcessor> _processors;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrintSpoolRetentionService> _logger;

    public PrintSpoolRetentionService(
        IOptions<PrintSpoolRetentionOptions> options,
        IEnumerable<IPrintSpoolRetentionProcessor> processors,
        TimeProvider timeProvider,
        ILogger<PrintSpoolRetentionService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(processors);
        _options = options.Value;
        _processors = processors.ToArray();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Enabled && _processors.Count != 1)
        {
            return Task.FromException(new InvalidOperationException(
                "Exactly one print spool retention processor must be registered when retention is enabled."));
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Print spool retention is disabled.");
            return;
        }

        var processor = _processors[0];
        var retention = _options.TerminalPayloadRetention!.Value;
        var maximumTotalBytes = _options.MaximumTotalBytes!.Value;
        var maximumDeletes = _options.MaximumDeletesPerRun!.Value;
        var runInterval = _options.RunInterval!.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = _timeProvider.GetUtcNow().ToUniversalTime();
                var deleteBefore = retention.Ticks >= now.Ticks
                    ? DateTimeOffset.MinValue
                    : now.Subtract(retention);
                var result = await processor.PruneAsync(
                    deleteBefore,
                    maximumTotalBytes,
                    maximumDeletes,
                    stoppingToken);
                if (result.TargetReached)
                {
                    _logger.LogInformation(
                        "Print spool retention deleted {DeletedPayloadCount} payloads and " +
                        "{DeletedBytes} bytes; {RemainingBytes} bytes remain.",
                        result.DeletedPayloadCount,
                        result.DeletedBytes,
                        result.After.TotalBytes);
                }
                else
                {
                    _logger.LogWarning(
                        "Print spool retention could not reach its quota; {RemainingBytes} bytes " +
                        "remain, including {ProtectedBytes} protected bytes and " +
                        "{AgeRetainedBytes} age-retained bytes.",
                        result.After.TotalBytes,
                        result.After.ProtectedBytes,
                        result.After.RetainedByAgeBytes);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogError(
                    "Print spool retention cycle failed with {ExceptionType}.",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(runInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
