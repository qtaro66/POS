using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Synchronization;

public enum BranchCloudOutboxCycleDisposition
{
    NoWork,
    Processed
}

public sealed record BranchCloudOutboxCycleResult(
    BranchCloudOutboxCycleDisposition Disposition);

public interface IBranchCloudOutboxCycleProcessor
{
    Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
        string branchId,
        string leaseOwner,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);
}

public interface IBranchCloudOutboxWorkerIdentity
{
    string WorkerId { get; }
}

public sealed class RuntimeBranchCloudOutboxWorkerIdentity :
    IBranchCloudOutboxWorkerIdentity
{
    public RuntimeBranchCloudOutboxWorkerIdentity(
        IOptions<BranchCloudOutboxDeliveryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var prefix = string.IsNullOrWhiteSpace(options.Value.WorkerId)
            ? "branch-cloud-outbox"
            : options.Value.WorkerId.Trim();
        WorkerId = $"{prefix}-{Environment.ProcessId}-{Guid.NewGuid():N}";
    }

    public string WorkerId { get; }
}

public sealed class BranchCloudOutboxPollingService : BackgroundService
{
    private readonly BranchCloudOutboxDeliveryOptions _options;
    private readonly IReadOnlyList<IBranchCloudOutboxCycleProcessor> _processors;
    private readonly IBranchCloudOutboxWorkerIdentity _identity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BranchCloudOutboxPollingService> _logger;

    public BranchCloudOutboxPollingService(
        IOptions<BranchCloudOutboxDeliveryOptions> options,
        IEnumerable<IBranchCloudOutboxCycleProcessor> processors,
        IBranchCloudOutboxWorkerIdentity identity,
        TimeProvider timeProvider,
        ILogger<BranchCloudOutboxPollingService> logger)
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
                "Exactly one Cloud outbox cycle processor must be registered when delivery is enabled."));
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Branch Cloud outbox delivery is disabled.");
            return;
        }

        var processor = _processors[0];
        var branchId = _options.BranchId.Trim();
        _logger.LogInformation(
            "Branch Cloud outbox polling started for {BranchId}.",
            branchId);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var result = await processor.ProcessNextAsync(
                    branchId,
                    _identity.WorkerId,
                    _options.LeaseDuration,
                    _options.RetryDelay,
                    stoppingToken);
                delay = result.Disposition switch
                {
                    BranchCloudOutboxCycleDisposition.NoWork => _options.IdleDelay,
                    BranchCloudOutboxCycleDisposition.Processed => TimeSpan.Zero,
                    _ => throw new InvalidOperationException(
                        "Unsupported Cloud outbox polling disposition.")
                };
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogError(
                    exception,
                    "Branch Cloud outbox polling cycle failed for {BranchId}.",
                    branchId);
                delay = _options.RetryDelay;
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

        _logger.LogInformation(
            "Branch Cloud outbox polling stopped for {BranchId}.",
            branchId);
    }
}
