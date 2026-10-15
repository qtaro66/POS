using System.Security.Cryptography;

namespace Pos.Printing.Abstractions;

public enum PrinterCondition
{
    Ready,
    Offline,
    CoverOpen,
    PaperOut,
    CutterError,
    StatusUnsupported,
    Unknown
}

public enum PrintDispatchOutcome
{
    Blocked,
    Failed,
    PrintDispatched,
    Unknown
}

public sealed record RenderedPrintPayload
{
    private readonly byte[] _bytes;

    public RenderedPrintPayload(string documentId, ReadOnlySpan<byte> bytes)
    {
        DocumentId = string.IsNullOrWhiteSpace(documentId)
            ? throw new ArgumentException("The document identifier cannot be empty.", nameof(documentId))
            : documentId.Trim();
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("The rendered print payload cannot be empty.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
        Sha256 = Convert.ToHexString(SHA256.HashData(_bytes));
    }

    public string DocumentId { get; }

    public string Sha256 { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes.ToArray();
}

public sealed record PrintDispatchRequest(
    string PrintJobId,
    string BranchId,
    string ProductionStationId,
    RenderedPrintPayload Payload);

public sealed record PrintDispatchResult(
    string PrintJobId,
    string? PrinterId,
    PrintDispatchOutcome Outcome,
    bool AttemptStarted,
    bool AutomaticRetryAllowed,
    string? BlockedReason,
    string? ErrorCode);

public interface IPrinterConfigurationProvider
{
    Task<PrinterProfile?> FindActivePrinterAsync(
        string branchId,
        string productionStationId,
        CancellationToken cancellationToken);
}

public interface IPrinterConditionProbe
{
    Task<PrinterCondition> GetConditionAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken);
}

public interface IPrintSendObserver
{
    void OnFirstByteSending();
}

public interface IPrintTransportAdapter
{
    PrinterProtocol Protocol { get; }

    Task SendAsync(
        PrinterProfile profile,
        RenderedPrintPayload payload,
        IPrintSendObserver observer,
        CancellationToken cancellationToken);
}

public interface IPrintDispatcher
{
    Task<PrintDispatchResult> DispatchAsync(
        PrintDispatchRequest request,
        CancellationToken cancellationToken = default);
}
