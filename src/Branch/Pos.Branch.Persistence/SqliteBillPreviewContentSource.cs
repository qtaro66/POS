using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Domain.Billing;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBillPreviewContentSource : ISqliteBillPreviewContentSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<bool> HasActiveOrderLinesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string branchId,
        string tableSessionId,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, transaction, """
            SELECT EXISTS (
                SELECT 1
                FROM order_lines
                WHERE branch_id = $branchId
                  AND table_session_id = $tableSessionId
                  AND status = 'Active'
                  AND active_quantity > 0);
            """);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$tableSessionId", tableSessionId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    public async Task<BillPreviewContent> CaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BillPreviewCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(connection, transaction.Connection))
        {
            throw new ArgumentException("The transaction must belong to the supplied connection.", nameof(transaction));
        }

        var policy = await ReadPolicyAsync(connection, transaction, request, cancellationToken);
        var storedLines = await ReadLinesAsync(connection, transaction, request, cancellationToken);
        var calculation = BillCalculator.Calculate(policy, storedLines.Select(line => line.Snapshot).ToArray());
        var linesById = storedLines.ToDictionary(line => line.Snapshot.OrderLineId, StringComparer.Ordinal);
        var payload = new BillPreviewPayload(
            request.BranchId,
            request.RequestId,
            request.TableSessionId.Value,
            request.TableId.Value,
            request.CapturedAt,
            calculation.Policy.MasterDataVersion,
            calculation.Policy.CalculationPolicyVersion,
            calculation.Policy.VatRate,
            calculation.Policy.ServiceChargeRate,
            calculation.Lines.Select(line => ToPayload(linesById[line.Snapshot.OrderLineId], line)).ToArray(),
            calculation.ItemGrossAmount,
            calculation.LineDiscountAmount,
            calculation.ItemNetAmount,
            ToPayload(calculation.Policy.BillDiscount, calculation.BillDiscount),
            calculation.ServiceChargeBase,
            calculation.ServiceChargeAmount,
            calculation.TotalBeforeRounding,
            calculation.RoundingAdjustment,
            calculation.AmountDue,
            calculation.VatInclusiveAmount,
            calculation.AmountBeforeVat);

        return BillPreviewContent.FromJson(1, JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private static async Task<BillPolicySnapshot> ReadPolicyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BillPreviewCaptureRequest request,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, transaction, """
            SELECT master_data_version, calculation_policy_version, vat_rate,
                   service_charge_rate, currency_scale, midpoint_rounding,
                   bill_discount_kind, bill_discount_requested_value
            FROM table_session_billing_policy
            WHERE branch_id = $branchId AND table_session_id = $tableSessionId;
            """);
        command.Parameters.AddWithValue("$branchId", request.BranchId);
        command.Parameters.AddWithValue("$tableSessionId", request.TableSessionId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The table session has no authoritative billing policy snapshot.");
        }

        var discount = ReadDiscount(reader, 6, 7);
        var policy = new BillPolicySnapshot(
            reader.GetString(0),
            reader.GetString(1),
            ParseDecimal(reader.GetString(2), "vat_rate"),
            ParseDecimal(reader.GetString(3), "service_charge_rate"),
            reader.GetInt32(4),
            ParseEnum<MonetaryRoundingMode>(reader.GetString(5), "midpoint_rounding"),
            discount);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The table session has duplicate billing policy snapshots.");
        }

        return policy;
    }

    private static async Task<IReadOnlyList<StoredLine>> ReadLinesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BillPreviewCaptureRequest request,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, transaction, """
            SELECT line.order_line_id, line.order_batch_id, line.sellable_item_id,
                   line.display_name, line.active_quantity, line.unit_price,
                   line.fulfillment_type, line.comment, line.discount_kind,
                   line.discount_requested_value, option.option_id, option.display_name,
                   option.quantity_per_item, option.unit_price
            FROM order_lines AS line
            INNER JOIN order_batches AS batch
                ON batch.branch_id = line.branch_id
               AND batch.order_batch_id = line.order_batch_id
            LEFT JOIN order_line_options AS option
                ON option.branch_id = line.branch_id
               AND option.order_line_id = line.order_line_id
            WHERE line.branch_id = $branchId
              AND line.table_session_id = $tableSessionId
              AND line.status = 'Active'
              AND line.active_quantity > 0
            ORDER BY COALESCE(batch.branch_sequence, 9223372036854775807),
                     batch.submitted_at, batch.order_batch_id,
                     line.line_sequence, option.option_sequence;
            """);
        command.Parameters.AddWithValue("$branchId", request.BranchId);
        command.Parameters.AddWithValue("$tableSessionId", request.TableSessionId.Value);

        var linesById = new Dictionary<string, StoredLineBuilder>(StringComparer.Ordinal);
        var orderedLines = new List<StoredLineBuilder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var lineId = reader.GetString(0);
            if (!linesById.TryGetValue(lineId, out var builder))
            {
                builder = new StoredLineBuilder(
                    lineId,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    ParseDecimal(reader.GetString(5), "unit_price"),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    ReadDiscount(reader, 8, 9));
                linesById.Add(lineId, builder);
                orderedLines.Add(builder);
            }

            if (!reader.IsDBNull(10))
            {
                builder.Options.Add(new OrderLineOptionSnapshot(
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetInt32(12),
                    ParseDecimal(reader.GetString(13), "option.unit_price")));
            }
        }

        return orderedLines.Select(builder => builder.Build()).ToArray();
    }

    private static DiscountSnapshot? ReadDiscount(SqliteDataReader reader, int kindOrdinal, int valueOrdinal)
    {
        var kindIsNull = reader.IsDBNull(kindOrdinal);
        var valueIsNull = reader.IsDBNull(valueOrdinal);
        if (kindIsNull && valueIsNull)
        {
            return null;
        }

        if (kindIsNull || valueIsNull)
        {
            throw new InvalidOperationException("A persisted discount must contain both kind and requested value.");
        }

        return new DiscountSnapshot(
            ParseEnum<DiscountKind>(reader.GetString(kindOrdinal), "discount_kind"),
            ParseDecimal(reader.GetString(valueOrdinal), "discount_requested_value"));
    }

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
            : throw new InvalidOperationException($"The database value in '{columnName}' is not a valid decimal.");

    private static TEnum ParseEnum<TEnum>(string value, string columnName)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"The database value in '{columnName}' is not valid.");

    private static PreviewDiscountPayload? ToPayload(
        DiscountSnapshot? snapshot,
        DiscountCalculation calculation) =>
        snapshot is null
            ? null
            : new PreviewDiscountPayload(
                snapshot.Kind.ToString(),
                snapshot.RequestedValue,
                calculation.RequestedAmount,
                calculation.AppliedAmount);

    private static PreviewLinePayload ToPayload(StoredLine storedLine, OrderLineCalculation calculation) =>
        new(
            calculation.Snapshot.OrderLineId,
            storedLine.OrderBatchId,
            calculation.Snapshot.SellableItemId,
            calculation.Snapshot.DisplayName,
            calculation.Snapshot.Quantity,
            calculation.Snapshot.UnitPrice,
            calculation.Snapshot.FulfillmentType,
            calculation.Snapshot.Comment,
            calculation.Snapshot.Options.Select(option => new PreviewOptionPayload(
                option.OptionId,
                option.DisplayName,
                option.QuantityPerItem,
                option.UnitPrice)).ToArray(),
            calculation.GrossAmount,
            ToPayload(calculation.Snapshot.Discount, calculation.Discount),
            calculation.NetAmount);

    private sealed class StoredLineBuilder(
        string orderLineId,
        string orderBatchId,
        string sellableItemId,
        string displayName,
        int quantity,
        decimal unitPrice,
        string fulfillmentType,
        string? comment,
        DiscountSnapshot? discount)
    {
        public List<OrderLineOptionSnapshot> Options { get; } = [];

        public StoredLine Build() => new(
            orderBatchId,
            new OrderLineSnapshot(
                orderLineId,
                sellableItemId,
                displayName,
                quantity,
                unitPrice,
                fulfillmentType,
                comment,
                Options,
                discount));
    }

    private sealed record StoredLine(string OrderBatchId, OrderLineSnapshot Snapshot);

    private sealed record BillPreviewPayload(
        string BranchId,
        string RequestId,
        string TableSessionId,
        string TableId,
        DateTimeOffset CapturedAt,
        string MasterDataVersion,
        string CalculationPolicyVersion,
        decimal VatRate,
        decimal ServiceChargeRate,
        IReadOnlyList<PreviewLinePayload> Items,
        decimal ItemGrossAmount,
        decimal LineDiscountAmount,
        decimal ItemNetAmount,
        PreviewDiscountPayload? BillDiscount,
        decimal ServiceChargeBase,
        decimal ServiceChargeAmount,
        decimal TotalBeforeRounding,
        decimal RoundingAdjustment,
        decimal NetTotal,
        decimal VatInclusiveAmount,
        decimal AmountBeforeVat);

    private sealed record PreviewLinePayload(
        string OrderLineId,
        string OrderBatchId,
        string SellableItemId,
        string DisplayName,
        int Quantity,
        decimal UnitPrice,
        string FulfillmentType,
        string? Comment,
        IReadOnlyList<PreviewOptionPayload> Options,
        decimal GrossAmount,
        PreviewDiscountPayload? Discount,
        decimal NetAmount);

    private sealed record PreviewOptionPayload(
        string OptionId,
        string DisplayName,
        int QuantityPerItem,
        decimal UnitPrice);

    private sealed record PreviewDiscountPayload(
        string Kind,
        decimal RequestedValue,
        decimal RequestedAmount,
        decimal AppliedAmount);
}
