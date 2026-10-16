using System.Globalization;
using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public sealed class EpsonTmT82xM352ASnapshotReader : IPrinterMibSnapshotReader
{
    private const string CoverStatusOid = "1.3.6.1.2.1.43.6.1.1.3.1.1";
    private const string PaperCurrentLevelOid = "1.3.6.1.2.1.43.8.2.1.10.1.1";
    private const string PaperStatusOid = "1.3.6.1.2.1.43.8.2.1.11.1.1";
    private const string AlertTableOid = "1.3.6.1.2.1.43.18.1.1";
    private const string StatusCommandProfileId = "snmp-printer-mib-v1";
    private const int AlertCodeColumn = 7;
    private const int AlertDescriptionColumn = 8;
    private const int MaximumAlertVariables = 256;
    private readonly ISnmpV1PrinterClient _client;

    public EpsonTmT82xM352ASnapshotReader(ISnmpV1PrinterClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<PrinterMibSnapshot> ReadAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.StatusMode is not PrinterStatusMode.SnmpPrinterMib ||
            !string.Equals(
                profile.CompatibilityProfileId,
                EpsonTmT82xM352AConditionEvaluator.ProfileId,
                StringComparison.Ordinal) ||
            !string.Equals(
                profile.StatusCommandProfile,
                StatusCommandProfileId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The printer profile is not supported by this snapshot reader.",
                nameof(profile));
        }

        var coverStatus = await ReadIntegerAsync(profile, CoverStatusOid, cancellationToken);
        var paperCurrentLevel = await ReadIntegerAsync(
            profile,
            PaperCurrentLevelOid,
            cancellationToken);
        var paperStatus = await ReadIntegerAsync(profile, PaperStatusOid, cancellationToken);
        var alertWalk = await _client.WalkAsync(
            profile,
            AlertTableOid,
            MaximumAlertVariables,
            cancellationToken);
        var parsedAlerts = ParseAlerts(alertWalk);

        return new PrinterMibSnapshot(
            coverStatus,
            paperCurrentLevel,
            paperStatus,
            parsedAlerts.IsComplete,
            parsedAlerts.Alerts);
    }

    private async Task<int> ReadIntegerAsync(
        PrinterProfile profile,
        string oid,
        CancellationToken cancellationToken)
    {
        var variable = await _client.GetAsync(profile, oid, cancellationToken);
        if (!string.Equals(variable.Oid, oid, StringComparison.Ordinal) ||
            variable.Value is not SnmpV1IntegerValue integer)
        {
            throw new InvalidDataException($"SNMP scalar '{oid}' returned an unexpected response.");
        }

        return integer.Value;
    }

    private static ParsedAlerts ParseAlerts(SnmpV1WalkResult walk)
    {
        ArgumentNullException.ThrowIfNull(walk);
        var codes = new Dictionary<AlertIndex, int>();
        var descriptions = new Dictionary<AlertIndex, string>();
        var observedIndexes = new HashSet<AlertIndex>();
        var isComplete = walk.CompletedSubtree;

        foreach (var variable in walk.Variables)
        {
            if (!TryGetAlertCell(variable.Oid, out var column, out var alertIndex))
            {
                isComplete = false;
                continue;
            }

            observedIndexes.Add(alertIndex);
            if (column == AlertCodeColumn)
            {
                if (variable.Value is not SnmpV1IntegerValue code ||
                    code.Value < 0 ||
                    !codes.TryAdd(alertIndex, code.Value))
                {
                    isComplete = false;
                }

                continue;
            }

            if (column == AlertDescriptionColumn)
            {
                if (variable.Value is not SnmpV1OctetStringValue description ||
                    !descriptions.TryAdd(alertIndex, description.Value))
                {
                    isComplete = false;
                }
            }
        }

        var indexes = observedIndexes
            .OrderBy(index => index.DeviceIndex)
            .ThenBy(index => index.AlertSequence);
        var alerts = new List<PrinterMibAlert>();
        foreach (var index in indexes)
        {
            if (!codes.TryGetValue(index, out var code) ||
                !descriptions.TryGetValue(index, out var description))
            {
                isComplete = false;
                continue;
            }

            alerts.Add(new PrinterMibAlert(code, description));
        }

        return new ParsedAlerts(isComplete, alerts);
    }

    private static bool TryGetAlertCell(
        string oid,
        out int column,
        out AlertIndex index)
    {
        var prefix = string.Concat(AlertTableOid, ".");
        if (!oid.StartsWith(prefix, StringComparison.Ordinal))
        {
            column = default;
            index = default;
            return false;
        }

        var components = oid[prefix.Length..].Split('.');
        if (components.Length != 3 ||
            !int.TryParse(
                components[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out column) ||
            column is < 1 or > 9 ||
            !uint.TryParse(
                components[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var deviceIndex) ||
            !uint.TryParse(
                components[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var alertIndex))
        {
            column = default;
            index = default;
            return false;
        }

        index = new AlertIndex(deviceIndex, alertIndex);
        return true;
    }

    private readonly record struct AlertIndex(uint DeviceIndex, uint AlertSequence);

    private sealed record ParsedAlerts(bool IsComplete, IReadOnlyList<PrinterMibAlert> Alerts);
}
