using System.Security.Claims;
using System.Text.Json;
using Pos.Application.MasterData;
using Pos.Application.Orders;
using Pos.Application.TableSessions;
using Pos.Branch.Api.Commands;
using Pos.Branch.Api.Security;
using Pos.Contracts.Ordering;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Ordering;

public static class BranchOrderingHttpHandlers
{
    public static async Task<IResult> OpenAsync(ClaimsPrincipal principal, BranchOpenTableRequest request,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        OpenTableCommand command;
        try
        {
            command = new(Context(request.SchemaVersion, request.Context, identity), TableId.From(Identifier(request.TableId)));
        }
        catch (ArgumentException) { return InvalidRequest(); }
        return Receipt(await runtime.Commands.OpenTableAsync(command, cancellationToken));
    }

    public static async Task<IResult> SubmitAsync(ClaimsPrincipal principal, BranchSubmitOrderRequest request,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        SubmitOrderCommand command;
        try
        {
            var metadata = Context(request.SchemaVersion, request.Context, identity);
            if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(line => line is null))
            {
                throw new ArgumentException("Invalid lines.");
            }
            var lines = request.Lines.Select(line =>
            {
                if (!Enum.TryParse<FulfillmentType>(line.FulfillmentType, false, out var fulfillment)
                    || !Enum.IsDefined(fulfillment) || fulfillment.ToString() != line.FulfillmentType)
                {
                    throw new ArgumentException("Invalid fulfillment type.");
                }
                if (line.Options?.Any(option => option is null) == true)
                {
                    throw new ArgumentException("Invalid options or comment.");
                }
                return new SubmitOrderLine(Identifier(line.SellableItemId), line.Quantity, fulfillment, line.Comment,
                    line.Options?.Select(option => new CatalogOptionSelection(Identifier(option.OptionGroupId),
                        Identifier(option.SellableItemId), option.Quantity)).ToArray());
            }).ToArray();
            command = new(metadata, $"{metadata.BranchId}:{metadata.RequestId}:batch:1", lines);
        }
        catch (ArgumentException) { return InvalidRequest(); }
        return Receipt(await runtime.Commands.SubmitOrderAsync(command, cancellationToken));
    }

    public static async Task<IResult> StatusAsync(ClaimsPrincipal principal, string requestId,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        try { requestId = Identifier(requestId); }
        catch (ArgumentException) { return InvalidRequest(); }
        var receipt = await runtime.Status.FindAsync(identity.BranchId, requestId, cancellationToken);
        return receipt is null
            ? TypedResults.NotFound(new BranchOrderingCommandResponse(
                BranchCommandStatusResponseMapper.Map(identity.BranchId, requestId, null), null))
            : TypedResults.Ok(Response(receipt));
    }

    public static async Task<IResult> TablesAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        var sessions = await runtime.Queries.ListActiveAsync(identity.BranchId, cancellationToken);
        return TypedResults.Ok(new BranchActiveTableResponse(1, false, sessions.Select(session =>
            new BranchTableSessionView(session.TableSessionId, session.TableId, session.Status, session.Version)).ToArray()));
    }

    public static async Task<IResult> MenuAsync(ClaimsPrincipal principal, string tableSessionId,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        try { tableSessionId = Identifier(tableSessionId); }
        catch (ArgumentException) { return InvalidRequest(); }
        var menu = await runtime.Queries.MenuAsync(identity.BranchId, tableSessionId, cancellationToken);
        if (menu is null) { return TypedResults.NotFound(new BranchOrderingError(1, "ActiveSessionMenuNotFound")); }
        return TypedResults.Ok(new BranchSessionMenuResponse(1, menu.TableSessionId, menu.MasterDataVersion,
            menu.Items.Select(context => Item(context.Item, context.CurrentAvailability, context.OptionGroups)).ToArray()));
    }

    private static BranchMenuItem Item(CatalogSellableItemSnapshot item,
        IReadOnlyDictionary<string, CurrentCatalogAvailability> availability, IReadOnlyList<CatalogOptionGroupSnapshot> groups)
    {
        var current = availability.GetValueOrDefault(item.SellableItemId) ?? new(false, false, false, false, false);
        return new(item.SellableItemId, item.Kind.ToString(), item.DisplayName, item.ResolvedPrice,
            new(current.ExistsInActivePackage, current.CentralActive, current.BranchCatalogEnabled, current.HasResolvedPrice, current.SoldOut),
            groups.Select(group => new BranchMenuOptionGroup(group.OptionGroupId, group.DisplayName, group.OptionType.ToString(),
                group.MinSelections, group.MaxSelections, group.AllowRepeatedOption, group.MaxQuantityPerOption,
                group.Options.Values.Select(option => Item(option, availability, [])).ToArray())).ToArray());
    }

    private static BranchCommandMetadata Context(int schemaVersion, BranchOrderingContext? context, AuthenticatedBranchRequestIdentity identity)
    {
        if (schemaVersion != 1 || context is null || context.ClientCreatedAt == default || context.ClientCreatedAt.Offset != TimeSpan.Zero
            || context.BranchId != identity.BranchId || context.DeviceId != identity.DeviceId || context.EmployeeId != identity.EmployeeId)
        {
            throw new ArgumentException("Invalid command identity or version.");
        }
        return new(Identifier(context.RequestId), identity.BranchId, identity.DeviceId, identity.EmployeeId,
            TableSessionId.From(Identifier(context.TableSessionId)), context.ClientCreatedAt);
    }

    private static string Identifier(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 128 || value != value.Trim() || value.Any(char.IsControl)
            ? throw new ArgumentException("Invalid identifier.") : value;

    private static IResult InvalidRequest() => TypedResults.BadRequest(new BranchOrderingError(1, "InvalidOrderingRequest"));
    private static IResult Receipt(BranchCommandReceipt receipt) => receipt.Outcome == BranchCommandOutcome.BranchAccepted
        ? TypedResults.Ok(Response(receipt)) : TypedResults.Conflict(Response(receipt));
    private static BranchOrderingCommandResponse Response(BranchCommandReceipt receipt) => new(
        BranchCommandStatusResponseMapper.Map(receipt.BranchId, receipt.RequestId, receipt),
        receipt.ErrorDetailsJson is null ? null : JsonSerializer.Deserialize<JsonElement>(receipt.ErrorDetailsJson));
}
