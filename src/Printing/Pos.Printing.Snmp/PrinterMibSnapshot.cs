namespace Pos.Printing.Snmp;

public sealed record PrinterMibAlert
{
    public PrinterMibAlert(int code, string description)
    {
        if (code < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Description = description is null
            ? throw new ArgumentNullException(nameof(description))
            : description.Trim();
    }

    public int Code { get; }

    public string Description { get; }
}

public sealed record PrinterMibSnapshot
{
    private readonly PrinterMibAlert[] _activeAlerts;

    public PrinterMibSnapshot(
        int coverStatus,
        int paperCurrentLevel,
        int paperStatus,
        bool alertTableComplete,
        IEnumerable<PrinterMibAlert> activeAlerts)
    {
        ArgumentNullException.ThrowIfNull(activeAlerts);
        CoverStatus = coverStatus;
        PaperCurrentLevel = paperCurrentLevel;
        PaperStatus = paperStatus;
        AlertTableComplete = alertTableComplete;
        _activeAlerts = activeAlerts.ToArray();
        if (_activeAlerts.Any(alert => alert is null))
        {
            throw new ArgumentException("The active-alert collection cannot contain null.", nameof(activeAlerts));
        }
    }

    public int CoverStatus { get; }

    public int PaperCurrentLevel { get; }

    public int PaperStatus { get; }

    public bool AlertTableComplete { get; }

    public IReadOnlyList<PrinterMibAlert> ActiveAlerts => _activeAlerts.ToArray();
}
