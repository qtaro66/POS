using System.Text.Json;
using System.Text.Json.Serialization;
using Pos.Contracts.Commands;

namespace Pos.Contracts.Ordering;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchOrderingContext(
    string RequestId, string BranchId, string DeviceId, string EmployeeId,
    string TableSessionId, DateTimeOffset ClientCreatedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchOpenTableRequest(int SchemaVersion, BranchOrderingContext Context, string TableId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchSubmitOrderRequest(
    int SchemaVersion, BranchOrderingContext Context,
    IReadOnlyList<BranchOrderLineRequest> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchOrderLineRequest(
    string SellableItemId, int Quantity, string FulfillmentType, string? Comment,
    IReadOnlyList<BranchOrderOptionRequest>? Options);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BranchOrderOptionRequest(string OptionGroupId, string SellableItemId, int Quantity);

public sealed record BranchOrderingCommandResponse(BranchCommandStatusResponse Result, JsonElement? ValidationDetails);
public sealed record BranchOrderingError(int SchemaVersion, string ErrorCode);
public sealed record BranchActiveTableResponse(
    int SchemaVersion, bool IncludesAvailableTables, IReadOnlyList<BranchTableSessionView> Sessions);
public sealed record BranchTableSessionView(string TableSessionId, string TableId, string Status, long TableVersion);
public sealed record BranchSessionMenuResponse(
    int SchemaVersion, string TableSessionId, string MasterDataVersion, IReadOnlyList<BranchMenuItem> Items);
public sealed record BranchMenuAvailability(
    bool ExistsInActivePackage, bool CentralActive, bool BranchEnabled, bool HasCurrentPrice, bool SoldOut);
public sealed record BranchMenuItem(
    string SellableItemId, string Kind, string DisplayName, decimal? UnitPrice,
    BranchMenuAvailability Availability, IReadOnlyList<BranchMenuOptionGroup> OptionGroups);
public sealed record BranchMenuOptionGroup(
    string OptionGroupId, string DisplayName, string OptionType, int MinSelections,
    int MaxSelections, bool AllowRepeatedOption, int MaxQuantityPerOption, IReadOnlyList<BranchMenuItem> Options);
