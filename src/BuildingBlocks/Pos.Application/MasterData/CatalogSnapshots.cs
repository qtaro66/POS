namespace Pos.Application.MasterData;

public enum CatalogItemKind
{
    Item,
    SubItem,
    Topping
}

public enum CatalogOptionType
{
    SubItem,
    Topping
}

public sealed record CatalogOptionSelection
{
    public CatalogOptionSelection(string optionGroupId, string sellableItemId, int quantity)
    {
        OptionGroupId = Require(optionGroupId, nameof(optionGroupId));
        SellableItemId = Require(sellableItemId, nameof(sellableItemId));
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Quantity = quantity;
    }

    public string OptionGroupId { get; }

    public string SellableItemId { get; }

    public int Quantity { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();
}

public sealed record CatalogSelectionRequest
{
    public CatalogSelectionRequest(
        string branchId,
        string tableSessionId,
        string sellableItemId,
        int quantity,
        IReadOnlyList<CatalogOptionSelection>? options = null)
    {
        BranchId = Require(branchId, nameof(branchId));
        TableSessionId = Require(tableSessionId, nameof(tableSessionId));
        SellableItemId = Require(sellableItemId, nameof(sellableItemId));
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Quantity = quantity;
        Options = options?.ToArray() ?? [];
        if (Options.Any(option => option is null))
        {
            throw new ArgumentException("Option selections cannot contain null values.", nameof(options));
        }
    }

    public string BranchId { get; }

    public string TableSessionId { get; }

    public string SellableItemId { get; }

    public int Quantity { get; }

    public IReadOnlyList<CatalogOptionSelection> Options { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();
}

public sealed record CatalogSellableItemSnapshot
{
    public CatalogSellableItemSnapshot(
        string sellableItemId,
        CatalogItemKind kind,
        string displayName,
        decimal? resolvedPrice,
        string? productionStationId = null)
    {
        SellableItemId = Require(sellableItemId, nameof(sellableItemId));
        DisplayName = Require(displayName, nameof(displayName));
        if (resolvedPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedPrice));
        }

        Kind = kind;
        ResolvedPrice = resolvedPrice;
        ProductionStationId = string.IsNullOrWhiteSpace(productionStationId)
            ? null
            : productionStationId.Trim();
    }

    public string SellableItemId { get; }

    public CatalogItemKind Kind { get; }

    public string DisplayName { get; }

    public decimal? ResolvedPrice { get; }

    public string? ProductionStationId { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();
}

public sealed record CatalogOptionGroupSnapshot
{
    public CatalogOptionGroupSnapshot(
        string optionGroupId,
        string displayName,
        CatalogOptionType optionType,
        int minSelections,
        int maxSelections,
        bool allowRepeatedOption,
        int maxQuantityPerOption,
        IReadOnlyDictionary<string, CatalogSellableItemSnapshot> options)
    {
        OptionGroupId = Require(optionGroupId, nameof(optionGroupId));
        DisplayName = Require(displayName, nameof(displayName));
        if (minSelections < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSelections));
        }

        if (maxSelections < minSelections)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSelections));
        }

        if (maxQuantityPerOption < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQuantityPerOption));
        }

        ArgumentNullException.ThrowIfNull(options);
        if (options.Count != options.Keys.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException("Option identifiers must be unique.", nameof(options));
        }

        var expectedKind = optionType == CatalogOptionType.SubItem
            ? CatalogItemKind.SubItem
            : CatalogItemKind.Topping;
        if (options.Values.Any(option => option.Kind != expectedKind))
        {
            throw new ArgumentException(
                "Every Option must have the same kind as its Option Group.",
                nameof(options));
        }

        OptionType = optionType;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        AllowRepeatedOption = allowRepeatedOption;
        MaxQuantityPerOption = maxQuantityPerOption;
        Options = new Dictionary<string, CatalogSellableItemSnapshot>(options, StringComparer.Ordinal);
    }

    public string OptionGroupId { get; }

    public string DisplayName { get; }

    public CatalogOptionType OptionType { get; }

    public int MinSelections { get; }

    public int MaxSelections { get; }

    public bool AllowRepeatedOption { get; }

    public int MaxQuantityPerOption { get; }

    public IReadOnlyDictionary<string, CatalogSellableItemSnapshot> Options { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();
}

public sealed record CurrentCatalogAvailability(
    bool ExistsInActivePackage,
    bool CentralActive,
    bool BranchCatalogEnabled,
    bool HasResolvedPrice,
    bool SoldOut);

public sealed record CatalogResolutionContext(
    string MasterDataVersion,
    CatalogSellableItemSnapshot Item,
    IReadOnlyList<CatalogOptionGroupSnapshot> OptionGroups,
    IReadOnlyDictionary<string, CurrentCatalogAvailability> CurrentAvailability);

public enum CatalogValidationErrorCode
{
    CatalogSnapshotUnavailable,
    CatalogItemNotFound,
    ResolvedPriceUnavailable,
    ItemNotInActivePackage,
    ItemCurrentPriceUnavailable,
    ItemCentralInactive,
    ItemBranchDisabled,
    ItemSoldOut,
    DuplicateOptionSelection,
    OptionGroupNotFound,
    OptionNotInGroup,
    OptionQuantityNotAllowed,
    OptionGroupBelowMinimum,
    OptionGroupAboveMaximum,
    OptionNotInActivePackage,
    OptionCurrentPriceUnavailable,
    OptionCentralInactive,
    OptionBranchDisabled,
    OptionSoldOut
}

public sealed record CatalogValidationError(
    CatalogValidationErrorCode Code,
    string? SellableItemId = null,
    string? OptionGroupId = null);

public sealed record ResolvedCatalogOption(
    string OptionGroupId,
    string SellableItemId,
    string DisplayName,
    int Quantity,
    decimal UnitPrice);

public sealed record ResolvedCatalogSelection(
    string MasterDataVersion,
    string SellableItemId,
    string DisplayName,
    int Quantity,
    decimal UnitPrice,
    IReadOnlyList<ResolvedCatalogOption> Options,
    string? ProductionStationId = null);

public sealed record CatalogResolutionResult(
    ResolvedCatalogSelection? Selection,
    IReadOnlyList<CatalogValidationError> Errors)
{
    public bool Accepted => Selection is not null && Errors.Count == 0;
}
