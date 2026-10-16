using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public interface IPrinterMibSnapshotReader
{
    Task<PrinterMibSnapshot> ReadAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken);
}

public interface IPrinterMibConditionEvaluator
{
    string CompatibilityProfileId { get; }

    PrinterCondition Evaluate(PrinterMibSnapshot snapshot);
}

public sealed class SnmpPrinterConditionProbe : IPrinterConditionProbe
{
    private readonly IPrinterMibSnapshotReader _reader;
    private readonly IReadOnlyDictionary<string, IPrinterMibConditionEvaluator> _evaluators;

    public SnmpPrinterConditionProbe(
        IPrinterMibSnapshotReader reader,
        IEnumerable<IPrinterMibConditionEvaluator> evaluators)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        ArgumentNullException.ThrowIfNull(evaluators);
        _evaluators = evaluators.ToDictionary(
            evaluator => evaluator.CompatibilityProfileId,
            StringComparer.Ordinal);
    }

    public async Task<PrinterCondition> GetConditionAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.StatusMode is PrinterStatusMode.Unsupported)
        {
            return PrinterCondition.StatusUnsupported;
        }

        if (profile.StatusMode is not PrinterStatusMode.SnmpPrinterMib ||
            !_evaluators.TryGetValue(profile.CompatibilityProfileId, out var evaluator))
        {
            return PrinterCondition.Unknown;
        }

        var snapshot = await _reader.ReadAsync(profile, cancellationToken);
        return evaluator.Evaluate(snapshot);
    }
}

public sealed class EpsonTmT82xM352AConditionEvaluator : IPrinterMibConditionEvaluator
{
    public const string ProfileId = "epson-tm-t82x-m352a-v1";
    private const int CoverOpen = 3;
    private const int CoverClosed = 4;
    private const int PaperOutCurrentLevel = 0;
    private const int PaperPresentCurrentLevel = -3;
    private const int PaperOutStatus = 19;
    private const int PaperPresentStatus = 0;
    private const int PaperOutAlertCode = 808;
    private const string PaperOutAlertDescription = "Roll Paper End";

    public string CompatibilityProfileId => ProfileId;

    public PrinterCondition Evaluate(PrinterMibSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.CoverStatus == CoverOpen)
        {
            return PrinterCondition.CoverOpen;
        }

        var exactPaperOutAlert = snapshot.ActiveAlerts.Any(IsExactPaperOutAlert);
        if ((snapshot.PaperCurrentLevel == PaperOutCurrentLevel &&
             snapshot.PaperStatus == PaperOutStatus) ||
            exactPaperOutAlert)
        {
            return PrinterCondition.PaperOut;
        }

        var verifiedReadyState =
            snapshot.CoverStatus == CoverClosed &&
            snapshot.PaperCurrentLevel == PaperPresentCurrentLevel &&
            snapshot.PaperStatus == PaperPresentStatus &&
            snapshot.AlertTableComplete &&
            snapshot.ActiveAlerts.Count == 0;
        return verifiedReadyState
            ? PrinterCondition.Ready
            : PrinterCondition.Unknown;
    }

    private static bool IsExactPaperOutAlert(PrinterMibAlert alert) =>
        alert.Code == PaperOutAlertCode &&
        string.Equals(
            alert.Description,
            PaperOutAlertDescription,
            StringComparison.Ordinal);
}
