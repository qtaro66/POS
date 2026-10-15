using System.Text.Json;
using System.Text.Json.Serialization;
using Pos.Application.MasterData;
using Pos.Application.Orders;
using Pos.Application.Printing;
using Pos.Application.Time;
using Pos.Domain.TableSessions;

namespace Pos.Application.TableSessions;

public sealed class TableSessionCommandService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly ITableSessionCommandStore _store;
    private readonly IBranchClock _clock;

    public TableSessionCommandService(
        ITableSessionCommandStore store,
        IBranchClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<BranchCommandReceipt> OpenTableAsync(
        OpenTableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Metadata);
        ArgumentNullException.ThrowIfNull(command.TableId);

        return _store.ExecuteAtomicallyAsync(async (transaction, token) =>
        {
            var duplicate = await transaction.FindReceiptAsync(
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                token);
            if (duplicate is not null)
            {
                return duplicate;
            }

            var receivedAt = _clock.UtcNow;
            var existing = await transaction.FindActiveSessionByTableAsync(
                command.Metadata.BranchId,
                command.TableId,
                token);
            if (existing is not null)
            {
                var existingReceipt = Accepted(command.Metadata, existing, receivedAt);
                await transaction.AddReceiptAsync(existingReceipt, token);
                return existingReceipt;
            }

            var activeMasterData = await transaction.FindActiveMasterDataSnapshotAsync(
                command.Metadata.BranchId,
                token);
            if (activeMasterData is null)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "ActiveMasterDataUnavailable",
                    token);
            }

            var session = TableSession.Open(
                command.Metadata.TableSessionId,
                command.TableId,
                TransitionContext(command.Metadata, receivedAt));
            await transaction.InsertSessionAsync(command.Metadata.BranchId, session, token);
            await transaction.AddTableSessionBillingPolicyAsync(
                command.Metadata.BranchId,
                session.Id,
                activeMasterData,
                receivedAt,
                token);

            var receipt = Accepted(command.Metadata, session, receivedAt);
            await PersistAcceptedTransitionAsync(
                transaction,
                command.Metadata,
                session,
                receipt,
                token);
            return receipt;
        }, cancellationToken);
    }

    public Task<BranchCommandReceipt> RequestPaymentAsync(
        RequestPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Metadata);

        return _store.ExecuteAtomicallyAsync(async (transaction, token) =>
        {
            var duplicate = await transaction.FindReceiptAsync(
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                token);
            if (duplicate is not null)
            {
                return duplicate;
            }

            var receivedAt = _clock.UtcNow;
            var session = await transaction.FindSessionAsync(
                command.Metadata.BranchId,
                command.Metadata.TableSessionId,
                token);
            if (session is null)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "TableSessionNotFound",
                    token);
            }

            if (session.Version != command.ExpectedVersion)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "TableVersionConflict",
                    token,
                    session);
            }

            var hasOpenCancellation = await transaction.HasOpenCancellationRequestAsync(
                command.Metadata.BranchId,
                command.Metadata.TableSessionId,
                token);

            if (session.Status != TableSessionStatus.Ordering)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "InvalidTableSessionState",
                    token,
                    session);
            }

            if (hasOpenCancellation)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "OpenCancellationRequest",
                    token,
                    session);
            }

            var hasActiveOrderLines = await transaction.HasActiveOrderLinesAsync(
                command.Metadata.BranchId,
                command.Metadata.TableSessionId,
                token);
            if (!hasActiveOrderLines)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "NoActiveOrderLines",
                    token,
                    session);
            }

            var captureRequest = new BillPreviewCaptureRequest(
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                session.Id,
                session.CurrentTableId,
                receivedAt);
            var previewContent = await transaction.CaptureBillPreviewContentAsync(captureRequest, token);

            try
            {
                session.RequestPayment(
                    hasOpenCancellation,
                    TransitionContext(command.Metadata, receivedAt));
            }
            catch (TableSessionRuleViolationException exception)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    exception.Rule,
                    token,
                    session);
            }
            catch (InvalidTableSessionTransitionException)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "InvalidTableSessionState",
                    token,
                    session);
            }

            await transaction.UpdateSessionAsync(
                command.Metadata.BranchId,
                session,
                command.ExpectedVersion,
                token);

            var snapshot = new BillPreviewSnapshot(
                $"{command.Metadata.BranchId}:{command.Metadata.RequestId}:bill-preview:1",
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                session.Id,
                session.CurrentTableId,
                "ใบตรวจสอบรายการ / ยังไม่ใช่ใบเสร็จ",
                previewContent,
                receivedAt);
            await transaction.AddBillPreviewSnapshotAsync(snapshot, token);
            await transaction.AddPrintJobAsync(
                new PersistentPrintJob(
                    $"{command.Metadata.BranchId}:{command.Metadata.RequestId}:cashier-print:1",
                    command.Metadata.BranchId,
                    command.Metadata.RequestId,
                    session.Id,
                    snapshot.SnapshotId,
                    PrintJobPurpose.BillPreview,
                    PrintTargetStation.Cashier,
                    PersistentPrintJobStatus.PrintQueued,
                    AttemptCount: 0,
                    receivedAt),
                token);
            var receipt = Accepted(command.Metadata, session, receivedAt);
            await PersistAcceptedTransitionAsync(
                transaction,
                command.Metadata,
                session,
                receipt,
                token);
            return receipt;
        }, cancellationToken);
    }

    public Task<BranchCommandReceipt> SubmitOrderAsync(
        SubmitOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _store.ExecuteAtomicallyAsync(async (transaction, token) =>
        {
            var metadata = command.Metadata;
            var duplicate = await transaction.FindReceiptAsync(
                metadata.BranchId,
                metadata.RequestId,
                token);
            if (duplicate is not null)
            {
                return duplicate;
            }

            var receivedAt = _clock.UtcNow;
            var session = await transaction.FindSessionAsync(
                metadata.BranchId,
                metadata.TableSessionId,
                token);
            if (session is null)
            {
                return await PersistRejectionAsync(
                    transaction,
                    metadata,
                    receivedAt,
                    "TableSessionNotFound",
                    token);
            }

            try
            {
                session.EnsureCanSubmitOrder();
            }
            catch (InvalidTableSessionTransitionException)
            {
                return await PersistRejectionAsync(
                    transaction,
                    metadata,
                    receivedAt,
                    "InvalidTableSessionState",
                    token,
                    session);
            }

            if (await transaction.OrderBatchExistsAsync(
                    metadata.BranchId,
                    command.OrderBatchId,
                    token))
            {
                return await PersistRejectionAsync(
                    transaction,
                    metadata,
                    receivedAt,
                    "OrderBatchIdConflict",
                    token,
                    session);
            }

            var resolvedLines = new List<ResolvedCatalogSelection>(command.Lines.Count);
            var validationErrors = new List<SubmitOrderValidationError>();
            for (var index = 0; index < command.Lines.Count; index++)
            {
                var line = command.Lines[index];
                var resolution = await transaction.ResolveCatalogSelectionAsync(
                    new CatalogSelectionRequest(
                        metadata.BranchId,
                        metadata.TableSessionId.Value,
                        line.SellableItemId,
                        line.Quantity,
                        line.Options),
                    token);
                if (!resolution.Accepted)
                {
                    validationErrors.AddRange(resolution.Errors.Select(error =>
                        new SubmitOrderValidationError(
                            index + 1,
                            error.Code,
                            error.SellableItemId,
                            error.OptionGroupId)));
                    continue;
                }

                resolvedLines.Add(resolution.Selection!);
            }

            if (validationErrors.Count != 0)
            {
                var detailsJson = JsonSerializer.Serialize(
                    new SubmitOrderRejectionDetails(1, validationErrors),
                    JsonOptions);
                return await PersistRejectionAsync(
                    transaction,
                    metadata,
                    receivedAt,
                    "CatalogValidationFailed",
                    token,
                    session,
                    detailsJson);
            }

            var branchSequence = await transaction.GetNextBranchSequenceAsync(
                metadata.BranchId,
                token);

            var snapshotLines = resolvedLines.Select((resolved, index) =>
            {
                var submitted = command.Lines[index];
                return new OrderLineSnapshot(
                    $"{metadata.BranchId}:{metadata.RequestId}:line:{index + 1}",
                    index + 1,
                    resolved.SellableItemId,
                    resolved.DisplayName,
                    resolved.Quantity,
                    resolved.UnitPrice,
                    submitted.FulfillmentType,
                    submitted.Comment,
                    resolved.Options.Select((option, optionIndex) =>
                        new OrderLineOptionSnapshot(
                            optionIndex + 1,
                            option.OptionGroupId,
                            option.SellableItemId,
                            option.DisplayName,
                            option.Quantity,
                            option.UnitPrice)).ToArray(),
                    resolved.ProductionStationId);
            }).ToArray();

            await transaction.AddOrderBatchAsync(
                new OrderBatchSnapshot(
                    metadata.BranchId,
                    command.OrderBatchId,
                    metadata.RequestId,
                    session.Id,
                    metadata.DeviceId,
                    metadata.EmployeeId,
                    branchSequence,
                    metadata.ClientCreatedAt,
                    receivedAt,
                    snapshotLines),
                token);

            var stationGroups = snapshotLines
                .Where(line => line.ProductionStationId is not null)
                .GroupBy(line => line.ProductionStationId!, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            for (var stationIndex = 0; stationIndex < stationGroups.Length; stationIndex++)
            {
                var station = stationGroups[stationIndex];
                var snapshotId =
                    $"{metadata.BranchId}:{metadata.RequestId}:production:{stationIndex + 1}";
                var payloadJson = JsonSerializer.Serialize(
                    new ProductionOrderPayload(
                        1,
                        command.OrderBatchId,
                        session.Id.Value,
                        session.CurrentTableId.Value,
                        station.Key,
                        receivedAt,
                        station.Select(line => new ProductionOrderLinePayload(
                            line.OrderLineId,
                            line.LineSequence,
                            line.DisplayName,
                            line.Quantity,
                            line.FulfillmentType,
                            line.Comment,
                            line.Options.Select(option => new ProductionOrderOptionPayload(
                                option.OptionGroupId,
                                option.DisplayName,
                                option.QuantityPerItem)).ToArray())).ToArray()),
                    JsonOptions);
                var content = ProductionOrderContent.FromJson(1, payloadJson);
                await transaction.AddProductionOrderSnapshotAsync(
                    new ProductionOrderSnapshot(
                        snapshotId,
                        metadata.BranchId,
                        metadata.RequestId,
                        command.OrderBatchId,
                        session.Id,
                        session.CurrentTableId,
                        station.Key,
                        content,
                        receivedAt),
                    token);
                await transaction.AddPrintJobAsync(
                    new PersistentPrintJob(
                        $"{metadata.BranchId}:{metadata.RequestId}:production-print:{stationIndex + 1}",
                        metadata.BranchId,
                        metadata.RequestId,
                        session.Id,
                        snapshotId,
                        PrintJobPurpose.KitchenOrder,
                        new PrintTargetStation(station.Key),
                        PersistentPrintJobStatus.PrintQueued,
                        0,
                        receivedAt),
                    token);
            }

            await transaction.AddCloudOutboxAsync(
                new CloudSyncOutboxRecord(
                    $"{metadata.BranchId}:{metadata.RequestId}:cloud:1",
                    metadata.BranchId,
                    metadata.RequestId,
                    session.Id,
                    TableSessionAction.SubmitOrder,
                    session.Version,
                    receivedAt),
                token);
            var receipt = Accepted(metadata, session, receivedAt) with
            {
                BranchSequence = branchSequence
            };
            await transaction.AddReceiptAsync(receipt, token);
            return receipt;
        }, cancellationToken);
    }

    private static async Task PersistAcceptedTransitionAsync(
        ITableSessionCommandTransaction transaction,
        BranchCommandMetadata metadata,
        TableSession session,
        BranchCommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        var transition = session.Transitions[^1];
        await transaction.AddAuditAsync(
            new TableSessionAuditRecord(
                $"{metadata.BranchId}:{metadata.RequestId}:audit:1",
                metadata.BranchId,
                metadata.RequestId,
                transition),
            cancellationToken);
        await transaction.AddCloudOutboxAsync(
            new CloudSyncOutboxRecord(
                $"{metadata.BranchId}:{metadata.RequestId}:cloud:1",
                metadata.BranchId,
                metadata.RequestId,
                session.Id,
                transition.Action,
                session.Version,
                receipt.ServerReceivedAt),
            cancellationToken);
        await transaction.AddReceiptAsync(receipt, cancellationToken);
    }

    private static async Task<BranchCommandReceipt> PersistRejectionAsync(
        ITableSessionCommandTransaction transaction,
        BranchCommandMetadata metadata,
        DateTimeOffset receivedAt,
        string errorCode,
        CancellationToken cancellationToken,
        TableSession? session = null,
        string? errorDetailsJson = null)
    {
        var receipt = new BranchCommandReceipt(
            metadata.RequestId,
            metadata.BranchId,
            metadata.DeviceId,
            metadata.EmployeeId,
            metadata.ClientCreatedAt,
            BranchCommandOutcome.Rejected,
            session?.Id ?? metadata.TableSessionId,
            session?.Status,
            session?.Version,
            errorCode,
            receivedAt,
            errorDetailsJson);
        await transaction.AddReceiptAsync(receipt, cancellationToken);
        return receipt;
    }

    private static BranchCommandReceipt Accepted(
        BranchCommandMetadata metadata,
        TableSession session,
        DateTimeOffset receivedAt)
    {
        return new BranchCommandReceipt(
            metadata.RequestId,
            metadata.BranchId,
            metadata.DeviceId,
            metadata.EmployeeId,
            metadata.ClientCreatedAt,
            BranchCommandOutcome.BranchAccepted,
            session.Id,
            session.Status,
            session.Version,
            ErrorCode: null,
            receivedAt);
    }

    private static TableSessionTransitionContext TransitionContext(
        BranchCommandMetadata metadata,
        DateTimeOffset receivedAt)
    {
        return new TableSessionTransitionContext(
            metadata.EmployeeId,
            metadata.DeviceId,
            receivedAt);
    }

    private sealed record SubmitOrderRejectionDetails(
        int SchemaVersion,
        IReadOnlyList<SubmitOrderValidationError> Errors);

    private sealed record ProductionOrderPayload(
        int SchemaVersion,
        string OrderBatchId,
        string TableSessionId,
        string TableId,
        string ProductionStationId,
        DateTimeOffset SubmittedAt,
        IReadOnlyList<ProductionOrderLinePayload> Lines);

    private sealed record ProductionOrderLinePayload(
        string OrderLineId,
        int LineSequence,
        string DisplayName,
        int Quantity,
        FulfillmentType FulfillmentType,
        string? Comment,
        IReadOnlyList<ProductionOrderOptionPayload> Options);

    private sealed record ProductionOrderOptionPayload(
        string OptionGroupId,
        string DisplayName,
        int QuantityPerItem);
}
