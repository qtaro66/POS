namespace Pos.Application.MasterData;

public static class CatalogSelectionValidator
{
    public static CatalogResolutionResult Validate(
        CatalogSelectionRequest request,
        CatalogResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var errors = new List<CatalogValidationError>();
        if (context.Item.ResolvedPrice is null)
        {
            errors.Add(new(
                CatalogValidationErrorCode.ResolvedPriceUnavailable,
                context.Item.SellableItemId));
        }

        ValidateAvailability(
            context.Item.SellableItemId,
            context.CurrentAvailability,
            isOption: false,
            errors);

        var groups = context.OptionGroups.ToDictionary(
            group => group.OptionGroupId,
            StringComparer.Ordinal);
        var duplicateSelections = request.Options
            .GroupBy(
                option => (
                    OptionGroupId: option.OptionGroupId,
                    SellableItemId: option.SellableItemId))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var duplicate in duplicateSelections)
        {
            errors.Add(new(
                CatalogValidationErrorCode.DuplicateOptionSelection,
                duplicate.SellableItemId,
                duplicate.OptionGroupId));
        }

        var resolvedOptions = new List<ResolvedCatalogOption>();
        foreach (var selection in request.Options)
        {
            if (!groups.TryGetValue(selection.OptionGroupId, out var group))
            {
                errors.Add(new(
                    CatalogValidationErrorCode.OptionGroupNotFound,
                    selection.SellableItemId,
                    selection.OptionGroupId));
                continue;
            }

            if (!group.Options.TryGetValue(selection.SellableItemId, out var option))
            {
                errors.Add(new(
                    CatalogValidationErrorCode.OptionNotInGroup,
                    selection.SellableItemId,
                    selection.OptionGroupId));
                continue;
            }

            var maximumQuantity = group.AllowRepeatedOption
                ? group.MaxQuantityPerOption
                : 1;
            if (selection.Quantity > maximumQuantity)
            {
                errors.Add(new(
                    CatalogValidationErrorCode.OptionQuantityNotAllowed,
                    selection.SellableItemId,
                    selection.OptionGroupId));
            }

            if (option.ResolvedPrice is null)
            {
                errors.Add(new(
                    CatalogValidationErrorCode.ResolvedPriceUnavailable,
                    option.SellableItemId,
                    selection.OptionGroupId));
            }

            ValidateAvailability(
                option.SellableItemId,
                context.CurrentAvailability,
                isOption: true,
                errors,
                selection.OptionGroupId);

            if (option.ResolvedPrice is not null)
            {
                resolvedOptions.Add(new(
                    selection.OptionGroupId,
                    option.SellableItemId,
                    option.DisplayName,
                    selection.Quantity,
                    option.ResolvedPrice.Value));
            }
        }

        foreach (var group in context.OptionGroups)
        {
            var selectionCount = request.Options
                .Where(option => string.Equals(
                    option.OptionGroupId,
                    group.OptionGroupId,
                    StringComparison.Ordinal))
                .Sum(option => (long)option.Quantity);
            if (selectionCount < group.MinSelections)
            {
                errors.Add(new(
                    CatalogValidationErrorCode.OptionGroupBelowMinimum,
                    OptionGroupId: group.OptionGroupId));
            }

            if (selectionCount > group.MaxSelections)
            {
                errors.Add(new(
                    CatalogValidationErrorCode.OptionGroupAboveMaximum,
                    OptionGroupId: group.OptionGroupId));
            }
        }

        if (errors.Count != 0 || context.Item.ResolvedPrice is null)
        {
            return new CatalogResolutionResult(null, errors);
        }

        return new CatalogResolutionResult(
            new ResolvedCatalogSelection(
                context.MasterDataVersion,
                context.Item.SellableItemId,
                context.Item.DisplayName,
                request.Quantity,
                context.Item.ResolvedPrice.Value,
                resolvedOptions),
            []);
    }

    private static void ValidateAvailability(
        string sellableItemId,
        IReadOnlyDictionary<string, CurrentCatalogAvailability> availabilityByItem,
        bool isOption,
        ICollection<CatalogValidationError> errors,
        string? optionGroupId = null)
    {
        if (!availabilityByItem.TryGetValue(sellableItemId, out var availability) ||
            !availability.ExistsInActivePackage)
        {
            errors.Add(new(
                isOption
                    ? CatalogValidationErrorCode.OptionNotInActivePackage
                    : CatalogValidationErrorCode.ItemNotInActivePackage,
                sellableItemId,
                optionGroupId));
            return;
        }

        if (!availability.HasResolvedPrice)
        {
            errors.Add(new(
                isOption
                    ? CatalogValidationErrorCode.OptionCurrentPriceUnavailable
                    : CatalogValidationErrorCode.ItemCurrentPriceUnavailable,
                sellableItemId,
                optionGroupId));
        }

        if (!availability.CentralActive)
        {
            errors.Add(new(
                isOption
                    ? CatalogValidationErrorCode.OptionCentralInactive
                    : CatalogValidationErrorCode.ItemCentralInactive,
                sellableItemId,
                optionGroupId));
        }

        if (!availability.BranchCatalogEnabled)
        {
            errors.Add(new(
                isOption
                    ? CatalogValidationErrorCode.OptionBranchDisabled
                    : CatalogValidationErrorCode.ItemBranchDisabled,
                sellableItemId,
                optionGroupId));
        }

        if (availability.SoldOut)
        {
            errors.Add(new(
                isOption
                    ? CatalogValidationErrorCode.OptionSoldOut
                    : CatalogValidationErrorCode.ItemSoldOut,
                sellableItemId,
                optionGroupId));
        }
    }
}
