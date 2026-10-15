namespace Pos.Printing.Abstractions;

public enum PrinterProtocol
{
    RawTcpEscPos,
    Ipp,
    Lpr
}

public enum PrinterRenderMode
{
    BitmapRaster,
    NativeTextExperimental
}

public enum PrinterStatusMode
{
    Unsupported,
    SnmpPrinterMib
}

public enum PrinterThaiEncodingMode
{
    UnicodeRaster,
    NativeCodePageExperimental
}

public sealed record PrinterProfile
{
    public PrinterProfile(
        string printerId,
        string displayName,
        string branchId,
        string productionStationId,
        string host,
        int port,
        PrinterProtocol protocol,
        string compatibilityProfileId,
        string manufacturer,
        string model,
        string? productCode,
        string? firmwareVersion,
        int paperWidthMillimeters,
        int dpi,
        int printableWidthDots,
        PrinterRenderMode renderMode,
        PrinterThaiEncodingMode thaiEncodingMode,
        PrinterStatusMode statusMode,
        bool supportsCashDrawer,
        string? cutterCommandHex,
        string? drawerPulseCommandHex,
        string? statusCommandProfile,
        TimeSpan connectTimeout,
        TimeSpan sendTimeout,
        int maximumAutomaticRetriesBeforeSend,
        TimeSpan automaticRetryBackoff)
    {
        PrinterId = Require(printerId, nameof(printerId));
        DisplayName = Require(displayName, nameof(displayName));
        BranchId = Require(branchId, nameof(branchId));
        ProductionStationId = Require(productionStationId, nameof(productionStationId));
        Host = Require(host, nameof(host));
        CompatibilityProfileId = Require(compatibilityProfileId, nameof(compatibilityProfileId));
        Manufacturer = Require(manufacturer, nameof(manufacturer));
        Model = Require(model, nameof(model));
        ProductCode = Optional(productCode);
        FirmwareVersion = Optional(firmwareVersion);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (!Enum.IsDefined(protocol))
        {
            throw new ArgumentOutOfRangeException(nameof(protocol));
        }

        if (!Enum.IsDefined(renderMode))
        {
            throw new ArgumentOutOfRangeException(nameof(renderMode));
        }

        if (!Enum.IsDefined(statusMode))
        {
            throw new ArgumentOutOfRangeException(nameof(statusMode));
        }

        if (!Enum.IsDefined(thaiEncodingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(thaiEncodingMode));
        }

        if (paperWidthMillimeters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters));
        }

        if (dpi < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi));
        }

        if (printableWidthDots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(printableWidthDots));
        }

        if (connectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        }

        if (sendTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sendTimeout));
        }

        if (maximumAutomaticRetriesBeforeSend < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAutomaticRetriesBeforeSend));
        }

        if (automaticRetryBackoff <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(automaticRetryBackoff));
        }

        CutterCommandHex = OptionalHex(cutterCommandHex, nameof(cutterCommandHex));
        DrawerPulseCommandHex = OptionalHex(drawerPulseCommandHex, nameof(drawerPulseCommandHex));
        StatusCommandProfile = Optional(statusCommandProfile);
        if (supportsCashDrawer != (DrawerPulseCommandHex is not null))
        {
            throw new ArgumentException(
                "Drawer capability and drawer pulse command must be configured together.",
                nameof(drawerPulseCommandHex));
        }

        if (statusMode != PrinterStatusMode.Unsupported && StatusCommandProfile is null)
        {
            throw new ArgumentException(
                "A status command profile is required when status support is enabled.",
                nameof(statusCommandProfile));
        }

        Port = port;
        Protocol = protocol;
        PaperWidthMillimeters = paperWidthMillimeters;
        Dpi = dpi;
        PrintableWidthDots = printableWidthDots;
        RenderMode = renderMode;
        ThaiEncodingMode = thaiEncodingMode;
        StatusMode = statusMode;
        SupportsCashDrawer = supportsCashDrawer;
        ConnectTimeout = connectTimeout;
        SendTimeout = sendTimeout;
        MaximumAutomaticRetriesBeforeSend = maximumAutomaticRetriesBeforeSend;
        AutomaticRetryBackoff = automaticRetryBackoff;
    }

    public string PrinterId { get; }
    public string DisplayName { get; }
    public string BranchId { get; }
    public string ProductionStationId { get; }
    public string Host { get; }
    public int Port { get; }
    public PrinterProtocol Protocol { get; }
    public string CompatibilityProfileId { get; }
    public string Manufacturer { get; }
    public string Model { get; }
    public string? ProductCode { get; }
    public string? FirmwareVersion { get; }
    public int PaperWidthMillimeters { get; }
    public int Dpi { get; }
    public int PrintableWidthDots { get; }
    public PrinterRenderMode RenderMode { get; }
    public PrinterThaiEncodingMode ThaiEncodingMode { get; }
    public PrinterStatusMode StatusMode { get; }
    public bool SupportsCashDrawer { get; }
    public string? CutterCommandHex { get; }
    public string? DrawerPulseCommandHex { get; }
    public string? StatusCommandProfile { get; }
    public TimeSpan ConnectTimeout { get; }
    public TimeSpan SendTimeout { get; }
    public int MaximumAutomaticRetriesBeforeSend { get; }
    public TimeSpan AutomaticRetryBackoff { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? OptionalHex(string? value, string name)
    {
        var normalized = Optional(value)?.ToUpperInvariant();
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length % 2 != 0 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The command must contain complete hexadecimal bytes.", name);
        }

        return normalized;
    }
}
