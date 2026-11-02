namespace Pos.Contracts.Printing;

public sealed record BranchPrinterInventoryResponse(int SchemaVersion, DateTimeOffset ObservedAt, long TotalCount, long ActiveCount,
    IReadOnlyList<BranchPrinterInventoryItemResponse> Items, string? NextAfterPrinterId);
public sealed record BranchPrinterInventoryItemResponse(
    string PrinterId, string DisplayName, string ProductionStationId, string? Host, int Port,
    string Protocol, string CompatibilityProfileId, string Manufacturer, string Model, string? ProductCode,
    string? FirmwareVersion, int PaperWidthMillimeters, string StatusMode, bool SupportsCashDrawer, bool IsActive, DateTimeOffset UpdatedAt);
public sealed record BranchPrinterInventoryError(int SchemaVersion, string Code);
