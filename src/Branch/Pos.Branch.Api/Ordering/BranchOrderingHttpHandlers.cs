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

    public static async Task<IResult> RequestPaymentAsync(ClaimsPrincipal principal, BranchRequestPaymentRequest request,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (request.ExpectedVersion < 1) { return InvalidRequest(); }
        RequestPaymentCommand command;
        try { command = new(Context(request.SchemaVersion, request.Context, identity), request.ExpectedVersion); }
        catch (ArgumentException) { return InvalidRequest(); }
        return Receipt(await runtime.Commands.RequestPaymentAsync(command, cancellationToken));
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

    public static async Task<IResult> QueueCancellationInspectionAsync(ClaimsPrincipal principal, string cancellationId,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (!AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        try { cancellationId = Identifier(cancellationId); }
        catch (ArgumentException) { return InvalidRequest(); }

        var jobId = await runtime.TableCommands.QueueCancellationInspectionAsync(
            identity.BranchId, cancellationId, runtime.Time.GetUtcNow(), cancellationToken);
        return jobId is null ? TypedResults.Conflict(new BranchOrderingError(1, "InspectionUnavailable"))
            : TypedResults.Ok(new { SchemaVersion = 1, PrintJobId = jobId });
    }

    public sealed record CancellationProductionLine(string OrderLineId, int? NotStarted, int? InProgress, int? Completed, int? NotApplicable);
    public sealed record CancellationProductionRequest(int SchemaVersion, IReadOnlyList<CancellationProductionLine>? Lines);

    public static async Task<IResult> RecordCancellationProductionAsync(ClaimsPrincipal principal, string cancellationId,
        CancellationProductionRequest request, BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (!AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        if (request.SchemaVersion != 1 || request.Lines is not { Count: > 0 }) { return InvalidRequest(); }
        var checks = new List<Pos.Application.TableSessions.CancellationProductionCheck>();
        try
        {
            cancellationId = Identifier(cancellationId);
            foreach (var line in request.Lines)
            {
                if (line is null || line.NotStarted is not int notStarted || line.InProgress is not int inProgress ||
                    line.Completed is not int completed || line.NotApplicable is not int notApplicable) { return InvalidRequest(); }
                checks.Add(new(Identifier(line.OrderLineId), notStarted, inProgress, completed, notApplicable));
            }
        }
        catch (ArgumentException) { return InvalidRequest(); }

        var recorded = await runtime.TableCommands.RecordCancellationProductionAsync(
            identity.BranchId, cancellationId, identity.EmployeeId, runtime.Time.GetUtcNow(), checks, cancellationToken);
        return recorded ? TypedResults.Ok(new { SchemaVersion = 1, Status = "StatusRecorded" })
            : TypedResults.Conflict(new BranchOrderingError(1, "InvalidCancellationProductionStatus"));
    }

    public static async Task<IResult> ConfirmCancellationAsync(ClaimsPrincipal principal, string cancellationId,
        CancellationProductionRequest request, BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (!AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        if (request.SchemaVersion != 1 || request.Lines is not { Count: > 0 }) { return InvalidRequest(); }
        var quantities = new List<Pos.Application.TableSessions.CancellationProductionCheck>();
        try
        {
            cancellationId = Identifier(cancellationId);
            foreach (var line in request.Lines)
            {
                if (line is null || line.NotStarted is not int notStarted || line.InProgress is not int inProgress ||
                    line.Completed is not int completed || line.NotApplicable is not int notApplicable) { return InvalidRequest(); }
                quantities.Add(new(Identifier(line.OrderLineId), notStarted, inProgress, completed, notApplicable));
            }
        }
        catch (ArgumentException) { return InvalidRequest(); }

        var status = await runtime.TableCommands.ConfirmCancellationAsync(
            identity.BranchId, cancellationId, identity.EmployeeId, runtime.Time.GetUtcNow(), quantities, cancellationToken);
        return status is null ? TypedResults.Conflict(new BranchOrderingError(1, "CancellationNotReadyForConfirmation"))
            : TypedResults.Ok(new { SchemaVersion = 1, Status = status });
    }

    public static async Task<IResult> RejectCancellationAsync(ClaimsPrincipal principal, string cancellationId,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (!AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        try { cancellationId = Identifier(cancellationId); }
        catch (ArgumentException) { return InvalidRequest(); }
        return await runtime.TableCommands.RejectCancellationAsync(identity.BranchId, cancellationId, cancellationToken)
            ? TypedResults.Ok(new { SchemaVersion = 1, Status = "Rejected" })
            : TypedResults.Conflict(new BranchOrderingError(1, "CancellationNotReadyForResolution"));
    }

    public static async Task<IResult> CancellationQueueAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        if (!AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        return TypedResults.Ok(new { SchemaVersion = 1, CanConfirm = configuration.GetValue<bool>("BranchOrdering:EnableCancellationConfirmation"), Requests = await runtime.Queries.CancellationQueueAsync(identity.BranchId, cancellationToken) });
    }

    public static async Task<IResult> FloorPlanAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        var plan = await runtime.Queries.FloorPlanAsync(identity.BranchId, cancellationToken);
        return plan is null
            ? TypedResults.Json(new BranchOrderingError(1, "TableRegistryUnavailable"), statusCode: 503)
            : TypedResults.Ok(new BranchFloorPlanResponse(1, plan.MasterDataVersion, plan.Tables));
    }

    public sealed record BranchFloorPlanResponse(int SchemaVersion, string MasterDataVersion, IReadOnlyList<Pos.Application.TableSessions.BranchFloorPlanTable> Tables);

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

    public static async Task<IResult> DetailAsync(ClaimsPrincipal principal, string tableSessionId,
        BranchOrderingRuntime runtime, CancellationToken cancellationToken)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity)) { return TypedResults.Unauthorized(); }
        try { tableSessionId = Identifier(tableSessionId); }
        catch (ArgumentException) { return InvalidRequest(); }
        var detail = await runtime.Queries.DetailAsync(identity.BranchId, tableSessionId, runtime.Time.GetUtcNow(), cancellationToken);
        if (detail is null) { return TypedResults.NotFound(new BranchOrderingError(1, "ActiveTableSessionNotFound")); }
        var totals = detail.BillPreviewTotals;
        return TypedResults.Ok(new BranchSessionDetailResponse(1,
            new(detail.Session.TableSessionId, detail.Session.TableId, detail.Session.Status, detail.Session.Version),
            detail.AsOf, detail.HasOpenCancellationRequest,
            totals is null ? null : new(totals.MasterDataVersion, totals.CalculationPolicyVersion, totals.VatRate, totals.ServiceChargeRate,
                totals.ItemGrossAmount, totals.LineDiscountAmount, totals.ItemNetAmount, totals.BillDiscountAmount,
                totals.ServiceChargeBase, totals.ServiceChargeAmount, totals.TotalBeforeRounding,
                totals.RoundingAdjustment, totals.NetTotal, totals.VatInclusiveAmount, totals.AmountBeforeVat),
            detail.Batches.Select(batch => new BranchAcceptedBatchView(
                batch.OrderBatchId, batch.RequestId, batch.BranchSequence, batch.ServerReceivedAt, "BranchAccepted",
                batch.Lines.Select(line => new BranchAcceptedLineView(
                    line.OrderLineId, line.LineSequence, line.SellableItemId, line.DisplayName,
                    line.OriginalQuantity, line.ActiveQuantity, line.Status, line.UnitPrice, line.FulfillmentType, line.Comment,
                    line.Options.Select(option => new BranchAcceptedOptionView(option.SellableItemId, option.OptionGroupId,
                        option.DisplayName, option.QuantityPerItem, option.UnitPrice)).ToArray(),
                    line.ActiveGrossAmount, line.ActiveDiscountAmount, line.ActiveNetAmount)).ToArray(),
                batch.ActiveGrossAmount, batch.ActiveLineDiscountAmount, batch.ActiveItemNetAmount)).ToArray()));
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
