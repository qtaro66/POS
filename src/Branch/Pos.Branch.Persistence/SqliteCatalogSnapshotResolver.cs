using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.MasterData;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteCatalogSnapshotResolver : ISqliteCatalogSnapshotResolver
{
    public async Task<CatalogResolutionResult> ResolveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogSelectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(connection, transaction.Connection))
        {
            throw new ArgumentException(
                "The transaction must belong to the supplied connection.",
                nameof(transaction));
        }

        var masterDataVersion = await ReadSessionMasterDataVersionAsync(
            connection,
            transaction,
            request,
            cancellationToken);
        if (masterDataVersion is null)
        {
            return Rejected(CatalogValidationErrorCode.CatalogSnapshotUnavailable);
        }

        var item = await ReadItemAsync(
            connection,
            transaction,
            request.BranchId,
            masterDataVersion,
            request.SellableItemId,
            cancellationToken);
        if (item is null)
        {
            return Rejected(
                CatalogValidationErrorCode.CatalogItemNotFound,
                request.SellableItemId);
        }

        var groups = await ReadGroupsAsync(
            connection,
            transaction,
            request.BranchId,
            masterDataVersion,
            request.SellableItemId,
            cancellationToken);
        var selectedItemIds = request.Options
            .Select(option => option.SellableItemId)
            .Append(request.SellableItemId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var availability = await ReadCurrentAvailabilityAsync(
            connection,
            transaction,
            request.BranchId,
            selectedItemIds,
            cancellationToken);

        return CatalogSelectionValidator.Validate(
            request,
            new CatalogResolutionContext(masterDataVersion, item, groups, availability));
    }

    private static async Task<string?> ReadSessionMasterDataVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogSelectionRequest request,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, transaction, """
            SELECT master_data_version
            FROM table_session_billing_policy
            WHERE branch_id = $branchId AND table_session_id = $tableSessionId;
            """);
        command.Parameters.AddWithValue("$branchId", request.BranchId);
        command.Parameters.AddWithValue("$tableSessionId", request.TableSessionId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<CatalogSellableItemSnapshot?> ReadItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string branchId,
        string masterDataVersion,
        string sellableItemId,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, transaction, """
            SELECT item_kind, display_name, resolved_price, production_station_id
            FROM master_data_sellable_items
            WHERE branch_id = $branchId
              AND master_data_version = $masterDataVersion
              AND sellable_item_id = $sellableItemId;
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$masterDataVersion", masterDataVersion);
        command.Parameters.AddWithValue("$sellableItemId", sellableItemId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var item = new CatalogSellableItemSnapshot(
            sellableItemId,
            ParseEnum<CatalogItemKind>(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : ParseDecimal(reader.GetString(2), "resolved_price"),
            reader.IsDBNull(3) ? null : reader.GetString(3));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The frozen catalog contains duplicate sellable items.");
        }

        return item;
    }

    private static async Task<IReadOnlyList<CatalogOptionGroupSnapshot>> ReadGroupsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string branchId,
        string masterDataVersion,
        string parentSellableItemId,
        CancellationToken cancellationToken)
    {
        var builders = new List<OptionGroupBuilder>();
        using (var command = CreateCommand(connection, transaction, """
            SELECT option_group_id, display_name, option_type, min_selections,
                   max_selections, allow_repeated_option, max_quantity_per_option
            FROM master_data_option_groups
            WHERE branch_id = $branchId
              AND master_data_version = $masterDataVersion
              AND parent_sellable_item_id = $parentSellableItemId
            ORDER BY display_order, option_group_id;
            """))
        {
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$masterDataVersion", masterDataVersion);
            command.Parameters.AddWithValue("$parentSellableItemId", parentSellableItemId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                builders.Add(new OptionGroupBuilder(
                    reader.GetString(0),
                    reader.GetString(1),
                    ParseEnum<CatalogOptionType>(reader.GetString(2)),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5) == 1,
                    reader.GetInt32(6)));
            }
        }

        foreach (var builder in builders)
        {
            using var command = CreateCommand(connection, transaction, """
                SELECT item.sellable_item_id, item.item_kind, item.display_name,
                       item.resolved_price
                FROM master_data_option_group_items AS member
                INNER JOIN master_data_sellable_items AS item
                    ON item.branch_id = member.branch_id
                   AND item.master_data_version = member.master_data_version
                   AND item.sellable_item_id = member.option_sellable_item_id
                WHERE member.branch_id = $branchId
                  AND member.master_data_version = $masterDataVersion
                  AND member.parent_sellable_item_id = $parentSellableItemId
                  AND member.option_group_id = $optionGroupId
                ORDER BY member.display_order, member.option_sellable_item_id;
                """);
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$masterDataVersion", masterDataVersion);
            command.Parameters.AddWithValue("$parentSellableItemId", parentSellableItemId);
            command.Parameters.AddWithValue("$optionGroupId", builder.OptionGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var option = new CatalogSellableItemSnapshot(
                    reader.GetString(0),
                    ParseEnum<CatalogItemKind>(reader.GetString(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : ParseDecimal(reader.GetString(3), "resolved_price"));
                if (!builder.Options.TryAdd(option.SellableItemId, option))
                {
                    throw new InvalidOperationException(
                        "The frozen catalog contains a duplicate option-group member.");
                }
            }
        }

        return builders.Select(builder => builder.Build()).ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, CurrentCatalogAvailability>>
        ReadCurrentAvailabilityAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string branchId,
            IReadOnlyList<string> sellableItemIds,
            CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CurrentCatalogAvailability>(StringComparer.Ordinal);
        foreach (var sellableItemId in sellableItemIds)
        {
            using var command = CreateCommand(connection, transaction, """
                SELECT item.central_active, item.branch_catalog_enabled,
                       item.resolved_price,
                       COALESCE(sold_out.is_sold_out, 0)
                FROM master_data_packages AS package
                INNER JOIN master_data_sellable_items AS item
                    ON item.branch_id = package.branch_id
                   AND item.master_data_version = package.master_data_version
                LEFT JOIN branch_sold_out_overrides AS sold_out
                    ON sold_out.branch_id = item.branch_id
                   AND sold_out.sellable_item_id = item.sellable_item_id
                WHERE package.branch_id = $branchId
                  AND package.lifecycle_status = 'Active'
                  AND item.sellable_item_id = $sellableItemId;
                """);
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$sellableItemId", sellableItemId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                result.Add(
                    sellableItemId,
                    new CurrentCatalogAvailability(false, false, false, false, false));
                continue;
            }

            result.Add(
                sellableItemId,
                new CurrentCatalogAvailability(
                    true,
                    reader.GetInt32(0) == 1,
                    reader.GetInt32(1) == 1,
                    ValidateCurrentPrice(reader, 2),
                    reader.GetInt32(3) == 1));
            if (await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The active catalog contains duplicate sellable items.");
            }
        }

        return result;
    }

    private static bool ValidateCurrentPrice(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        var price = ParseDecimal(reader.GetString(ordinal), "resolved_price");
        if (price < 0m)
        {
            throw new InvalidOperationException(
                "The active catalog contains a negative resolved price.");
        }

        return true;
    }

    private static CatalogResolutionResult Rejected(
        CatalogValidationErrorCode code,
        string? sellableItemId = null) =>
        new(null, [new CatalogValidationError(code, sellableItemId)]);

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static decimal ParseDecimal(string value, string columnName) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"The database value in '{columnName}' is not a valid decimal.");

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new InvalidOperationException(
                $"The database value '{value}' is not a valid {typeof(TEnum).Name}.");
        }

        return parsed;
    }

    private sealed class OptionGroupBuilder(
        string optionGroupId,
        string displayName,
        CatalogOptionType optionType,
        int minSelections,
        int maxSelections,
        bool allowRepeatedOption,
        int maxQuantityPerOption)
    {
        public string OptionGroupId { get; } = optionGroupId;

        public Dictionary<string, CatalogSellableItemSnapshot> Options { get; } =
            new(StringComparer.Ordinal);

        public CatalogOptionGroupSnapshot Build() =>
            new(
                OptionGroupId,
                displayName,
                optionType,
                minSelections,
                maxSelections,
                allowRepeatedOption,
                maxQuantityPerOption,
                Options);
    }
}
