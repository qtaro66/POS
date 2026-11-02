namespace Pos.Application.Printing;

public sealed record BranchPrinterInventoryItem(
    string PrinterId, string DisplayName, string ProductionStationId, string Host, int Port,
    string Protocol, string CompatibilityProfileId, string Manufacturer, string Model, string? ProductCode,
    string? FirmwareVersion, int PaperWidthMillimeters, string StatusMode, bool SupportsCashDrawer, bool IsActive, DateTimeOffset UpdatedAt);

public sealed record BranchPrinterInventoryPage(long TotalCount, long ActiveCount, IReadOnlyList<BranchPrinterInventoryItem> Items, string? NextAfterPrinterId);

public interface IBranchPrinterInventoryQuery
{
    Task<BranchPrinterInventoryPage> ListAsync(string branchId, int maximumResults, string? afterPrinterId = null, CancellationToken cancellationToken = default);
}
