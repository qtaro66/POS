using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Printing;

public enum PrintPollingCycleDisposition
{
    NoWork,
    Blocked,
    Processed
}

public sealed record PrintPollingCycleResult(
    PrintPollingCycleDisposition Disposition,
    string? Detail = null);

public interface IPrintSpoolCycleProcessor
{
    Task<PrintPollingCycleResult> ProcessNextAsync(
        string printerId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}

public sealed class PersistedPrintSpoolCycleProcessor : IPrintSpoolCycleProcessor
{
    private readonly PersistedPrintSpoolWorker _worker;

    public PersistedPrintSpoolCycleProcessor(PersistedPrintSpoolWorker worker)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
    }

    public async Task<PrintPollingCycleResult> ProcessNextAsync(
        string printerId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var result = await _worker.ProcessNextAsync(
            printerId,
            workerId,
            leaseDuration,
            cancellationToken);
        return result.Outcome switch
        {
            PrintSpoolCycleOutcome.NoWork =>
                new PrintPollingCycleResult(PrintPollingCycleDisposition.NoWork),
            PrintSpoolCycleOutcome.Blocked =>
                new PrintPollingCycleResult(PrintPollingCycleDisposition.Blocked, result.Detail),
            PrintSpoolCycleOutcome.Failed or
            PrintSpoolCycleOutcome.PrintDispatched or
            PrintSpoolCycleOutcome.Unknown =>
                new PrintPollingCycleResult(PrintPollingCycleDisposition.Processed, result.Detail),
            _ => throw new InvalidOperationException("Unsupported print spool cycle outcome.")
        };
    }
}
