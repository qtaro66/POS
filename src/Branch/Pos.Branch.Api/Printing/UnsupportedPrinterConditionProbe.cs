using Pos.Printing.Abstractions;

namespace Pos.Branch.Api.Printing;

public sealed class UnsupportedPrinterConditionProbe : IPrinterConditionProbe
{
    public Task<PrinterCondition> GetConditionAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            profile.StatusMode is PrinterStatusMode.Unsupported
                ? PrinterCondition.StatusUnsupported
                : PrinterCondition.Unknown);
    }
}
