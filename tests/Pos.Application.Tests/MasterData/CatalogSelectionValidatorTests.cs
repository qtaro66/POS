using Pos.Application.MasterData;

namespace Pos.Application.Tests.MasterData;

public sealed class CatalogSelectionValidatorTests
{
    [Fact]
    public void Accepts_selection_and_snapshots_frozen_names_and_prices()
    {
        var request = Request(
            new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 2));
        var context = Context(
            itemPrice: 105m,
            group: Group(min: 1, max: 3, repeated: true, maxPerOption: 2),
            availability: Available("ITEM-1", "OPT-CHICKEN"));

        var result = CatalogSelectionValidator.Validate(request, context);

        Assert.True(result.Accepted);
        Assert.Empty(result.Errors);
        Assert.Equal("MASTER-FROZEN", result.Selection!.MasterDataVersion);
        Assert.Equal("ผัดเปรี้ยวหวาน", result.Selection.DisplayName);
        Assert.Equal(105m, result.Selection.UnitPrice);
        var option = Assert.Single(result.Selection.Options);
        Assert.Equal("ไก่ชิ้น", option.DisplayName);
        Assert.Equal(2, option.Quantity);
        Assert.Equal(30m, option.UnitPrice);
    }

    [Fact]
    public void Counts_selection_quantities_for_group_minimum_and_maximum()
    {
        var context = Context(
            group: Group(min: 2, max: 2, repeated: true, maxPerOption: 2),
            availability: Available("ITEM-1", "OPT-CHICKEN"));

        var accepted = CatalogSelectionValidator.Validate(
            Request(new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 2)),
            context);
        var belowMinimum = CatalogSelectionValidator.Validate(Request(), context);
        var aboveMaximum = CatalogSelectionValidator.Validate(
            Request(new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 3)),
            context);

        Assert.True(accepted.Accepted);
        Assert.Contains(
            belowMinimum.Errors,
            error => error.Code == CatalogValidationErrorCode.OptionGroupBelowMinimum);
        Assert.Contains(
            aboveMaximum.Errors,
            error => error.Code == CatalogValidationErrorCode.OptionGroupAboveMaximum);
    }

    [Fact]
    public void Rejects_repeated_option_when_group_does_not_allow_it()
    {
        var result = CatalogSelectionValidator.Validate(
            Request(new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 2)),
            Context(
                group: Group(min: 0, max: 3, repeated: false, maxPerOption: 5),
                availability: Available("ITEM-1", "OPT-CHICKEN")));

        Assert.False(result.Accepted);
        Assert.Contains(
            result.Errors,
            error => error.Code == CatalogValidationErrorCode.OptionQuantityNotAllowed);
    }

    [Fact]
    public void Accepts_zero_prices()
    {
        var zeroPriceOption = new CatalogSellableItemSnapshot(
            "OPT-CHICKEN",
            CatalogItemKind.SubItem,
            "ไก่ชิ้น",
            0m);
        var group = new CatalogOptionGroupSnapshot(
            "PROTEIN",
            "เลือกวัตถุดิบหลัก",
            CatalogOptionType.SubItem,
            0,
            1,
            false,
            1,
            new Dictionary<string, CatalogSellableItemSnapshot>(StringComparer.Ordinal)
            {
                [zeroPriceOption.SellableItemId] = zeroPriceOption
            });

        var result = CatalogSelectionValidator.Validate(
            Request(new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 1)),
            Context(
                itemPrice: 0m,
                group: group,
                availability: Available("ITEM-1", "OPT-CHICKEN")));

        Assert.True(result.Accepted);
        Assert.Equal(0m, result.Selection!.UnitPrice);
        Assert.Equal(0m, Assert.Single(result.Selection.Options).UnitPrice);
    }

    [Fact]
    public void Rejects_duplicate_option_rows_instead_of_combining_them()
    {
        var duplicate = new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 1);

        var result = CatalogSelectionValidator.Validate(
            Request(duplicate, duplicate),
            Context(availability: Available("ITEM-1", "OPT-CHICKEN")));

        Assert.False(result.Accepted);
        Assert.Contains(
            result.Errors,
            error => error.Code == CatalogValidationErrorCode.DuplicateOptionSelection);
    }

    [Theory]
    [InlineData(false, true, true, true, false, CatalogValidationErrorCode.ItemNotInActivePackage)]
    [InlineData(true, false, true, true, false, CatalogValidationErrorCode.ItemCentralInactive)]
    [InlineData(true, true, false, true, false, CatalogValidationErrorCode.ItemBranchDisabled)]
    [InlineData(true, true, true, true, true, CatalogValidationErrorCode.ItemSoldOut)]
    [InlineData(true, true, true, false, false, CatalogValidationErrorCode.ItemCurrentPriceUnavailable)]
    public void Rejects_item_using_current_sellability(
        bool exists,
        bool centralActive,
        bool branchEnabled,
        bool hasPrice,
        bool soldOut,
        CatalogValidationErrorCode expected)
    {
        var availability = Available("OPT-CHICKEN").ToDictionary(pair => pair.Key, pair => pair.Value);
        availability["ITEM-1"] = new(
            exists,
            centralActive,
            branchEnabled,
            hasPrice,
            soldOut);

        var result = CatalogSelectionValidator.Validate(
            Request(new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 1)),
            Context(availability: availability));

        Assert.False(result.Accepted);
        Assert.Contains(result.Errors, error => error.Code == expected);
    }

    [Fact]
    public void Rejects_option_not_in_frozen_group_and_missing_frozen_price()
    {
        var unavailablePriceOption = new CatalogSellableItemSnapshot(
            "OPT-CHICKEN",
            CatalogItemKind.SubItem,
            "ไก่ชิ้น",
            null);
        var group = new CatalogOptionGroupSnapshot(
            "PROTEIN",
            "เลือกวัตถุดิบหลัก",
            CatalogOptionType.SubItem,
            0,
            2,
            false,
            1,
            new Dictionary<string, CatalogSellableItemSnapshot>(StringComparer.Ordinal)
            {
                [unavailablePriceOption.SellableItemId] = unavailablePriceOption
            });

        var missingPrice = CatalogSelectionValidator.Validate(
            Request(new CatalogOptionSelection("PROTEIN", "OPT-CHICKEN", 1)),
            Context(group: group, availability: Available("ITEM-1", "OPT-CHICKEN")));
        var notInGroup = CatalogSelectionValidator.Validate(
            Request(new CatalogOptionSelection("PROTEIN", "OPT-SHRIMP", 1)),
            Context(group: group, availability: Available("ITEM-1", "OPT-SHRIMP")));

        Assert.Contains(
            missingPrice.Errors,
            error => error.Code == CatalogValidationErrorCode.ResolvedPriceUnavailable);
        Assert.Contains(
            notInGroup.Errors,
            error => error.Code == CatalogValidationErrorCode.OptionNotInGroup);
    }

    private static CatalogSelectionRequest Request(params CatalogOptionSelection[] options) =>
        new("BRANCH-01", "TS-1", "ITEM-1", 1, options);

    private static CatalogResolutionContext Context(
        decimal? itemPrice = 105m,
        CatalogOptionGroupSnapshot? group = null,
        IReadOnlyDictionary<string, CurrentCatalogAvailability>? availability = null) =>
        new(
            "MASTER-FROZEN",
            new CatalogSellableItemSnapshot(
                "ITEM-1",
                CatalogItemKind.Item,
                "ผัดเปรี้ยวหวาน",
                itemPrice),
            [group ?? Group()],
            availability ?? Available("ITEM-1", "OPT-CHICKEN"));

    private static CatalogOptionGroupSnapshot Group(
        int min = 0,
        int max = 2,
        bool repeated = false,
        int maxPerOption = 1)
    {
        var option = new CatalogSellableItemSnapshot(
            "OPT-CHICKEN",
            CatalogItemKind.SubItem,
            "ไก่ชิ้น",
            30m);
        return new CatalogOptionGroupSnapshot(
            "PROTEIN",
            "เลือกวัตถุดิบหลัก",
            CatalogOptionType.SubItem,
            min,
            max,
            repeated,
            maxPerOption,
            new Dictionary<string, CatalogSellableItemSnapshot>(StringComparer.Ordinal)
            {
                [option.SellableItemId] = option
            });
    }

    private static IReadOnlyDictionary<string, CurrentCatalogAvailability> Available(
        params string[] ids) =>
        ids.ToDictionary(
            id => id,
            _ => new CurrentCatalogAvailability(true, true, true, true, false),
            StringComparer.Ordinal);
}
