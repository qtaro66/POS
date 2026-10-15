using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pos.Printing.Abstractions;

namespace Pos.Printing.EscPosTcp;

public sealed class EscPosRasterPayloadRenderer : IPrintPayloadRenderer
{
    private const int MaximumJsonBytes = 1_048_576;
    private const int MaximumRasterChunkRows = 2_048;
    private static readonly byte[] InitializePrinter = [0x1B, 0x40];
    private static readonly byte[] FeedThreeLines = [0x1B, 0x64, 0x03];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly IEscPosDocumentRasterizer _rasterizer;

    public EscPosRasterPayloadRenderer(IEscPosDocumentRasterizer rasterizer)
    {
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
    }

    public Task<RenderedPrintPayload> RenderAsync(
        PrintRenderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProfile(request.Printer);
        ValidateSource(request.Document);

        var lines = request.Document.Kind switch
        {
            PrintDocumentKind.BillPreview => BuildBillPreview(request.Document.ContentJson),
            PrintDocumentKind.KitchenOrder => BuildKitchenOrder(request.Document.ContentJson),
            _ => throw new InvalidDataException("Unsupported print document kind.")
        };
        var raster = _rasterizer.Rasterize(
            lines,
            request.Printer.PrintableWidthDots,
            cancellationToken);
        if (raster.WidthDots != request.Printer.PrintableWidthDots)
        {
            throw new InvalidDataException("The raster width does not match the printer profile.");
        }

        var payload = Encode(raster, request.Printer.CutterCommandHex, cancellationToken);
        return Task.FromResult(new RenderedPrintPayload(request.Document.DocumentId, payload));
    }

    private static IReadOnlyList<EscPosTextLine> BuildBillPreview(string json)
    {
        var bill = Deserialize<BillPreviewDocument>(json);
        Require(bill.BranchId, "branchId");
        Require(bill.RequestId, "requestId");
        Require(bill.TableSessionId, "tableSessionId");
        Require(bill.TableId, "tableId");
        Require(bill.MasterDataVersion, "masterDataVersion");
        Require(bill.CalculationPolicyVersion, "calculationPolicyVersion");
        if (bill.CapturedAt == default)
        {
            throw new InvalidDataException("capturedAt is required.");
        }

        Percentage(bill.VatRate, "vatRate");
        Percentage(bill.ServiceChargeRate, "serviceChargeRate");
        NonNegative(bill.ItemGrossAmount, "itemGrossAmount");
        NonNegative(bill.LineDiscountAmount, "lineDiscountAmount");
        NonNegative(bill.ItemNetAmount, "itemNetAmount");
        NonNegative(bill.ServiceChargeBase, "serviceChargeBase");
        NonNegative(bill.ServiceChargeAmount, "serviceChargeAmount");
        NonNegative(bill.TotalBeforeRounding, "totalBeforeRounding");
        NonNegative(bill.NetTotal, "netTotal");
        NonNegative(bill.VatInclusiveAmount, "vatInclusiveAmount");
        NonNegative(bill.AmountBeforeVat, "amountBeforeVat");
        Validate(bill.BillDiscount);
        if (bill.Items is not { Count: > 0 })
        {
            throw new InvalidDataException("Bill Preview must contain at least one item.");
        }

        var lines = new List<EscPosTextLine>
        {
            new("ใบตรวจสอบรายการ", FontSize: 36, Bold: true, Alignment: EscPosTextAlignment.Center),
            new("ยังไม่ใช่ใบเสร็จ", FontSize: 30, Bold: true, Alignment: EscPosTextAlignment.Center),
            new($"โต๊ะ {bill.TableId}", $"{bill.CapturedAt:dd/MM/yyyy HH:mm}", 27, true, TopSpacingDots: 10),
            new(new string('-', 42), Alignment: EscPosTextAlignment.Center)
        };
        foreach (var item in bill.Items)
        {
            Validate(item);
            lines.Add(new EscPosTextLine(
                $"{item.Quantity} x {item.DisplayName} [{Fulfillment(item.FulfillmentType!)}]",
                Money(item.NetAmount),
                Bold: true,
                TopSpacingDots: 4));
            foreach (var option in item.Options ?? [])
            {
                Require(option.OptionId, "optionId");
                Require(option.DisplayName, "option displayName");
                Positive(option.QuantityPerItem, "option quantityPerItem");
                NonNegative(option.UnitPrice, "option unitPrice");
                lines.Add(new EscPosTextLine(
                    $"  + {option.DisplayName} x{option.QuantityPerItem}",
                    option.UnitPrice == 0 ? null : Money(option.UnitPrice * option.QuantityPerItem * item.Quantity),
                    24));
            }

            if (!string.IsNullOrWhiteSpace(item.Comment))
            {
                lines.Add(new EscPosTextLine($"  หมายเหตุ: {item.Comment.Trim()}", FontSize: 24));
            }

            if (item.Discount is not null)
            {
                lines.Add(new EscPosTextLine("  ส่วนลดรายการ", $"-{Money(item.Discount.AppliedAmount)}", 24));
            }
        }

        lines.Add(new EscPosTextLine(new string('-', 42), Alignment: EscPosTextAlignment.Center));
        lines.Add(new EscPosTextLine("รวมรายการ", Money(bill.ItemGrossAmount)));
        if (bill.LineDiscountAmount != 0)
        {
            lines.Add(new EscPosTextLine("ส่วนลดรายการ", $"-{Money(bill.LineDiscountAmount)}"));
        }

        if (bill.BillDiscount is not null && bill.BillDiscount.AppliedAmount != 0)
        {
            lines.Add(new EscPosTextLine("ส่วนลดท้ายบิล", $"-{Money(bill.BillDiscount.AppliedAmount)}"));
        }

        if (bill.ServiceChargeAmount != 0)
        {
            lines.Add(new EscPosTextLine("Service Charge", Money(bill.ServiceChargeAmount)));
        }

        if (bill.RoundingAdjustment != 0)
        {
            lines.Add(new EscPosTextLine("ปรับเศษ", SignedMoney(bill.RoundingAdjustment)));
        }

        lines.Add(new EscPosTextLine("ยอดสุทธิ", Money(bill.NetTotal), 34, true, TopSpacingDots: 6));
        lines.Add(new EscPosTextLine(
            $"VAT รวมในราคา {Percent(bill.VatRate)}",
            Money(bill.VatInclusiveAmount),
            23));
        return lines;
    }

    private static IReadOnlyList<EscPosTextLine> BuildKitchenOrder(string json)
    {
        var order = Deserialize<KitchenOrderDocument>(json);
        if (order.SchemaVersion != 1)
        {
            throw new InvalidDataException("Unsupported Kitchen Order payload schema version.");
        }

        Require(order.OrderBatchId, "orderBatchId");
        Require(order.TableSessionId, "tableSessionId");
        Require(order.TableId, "tableId");
        Require(order.ProductionStationId, "productionStationId");
        if (order.SubmittedAt == default)
        {
            throw new InvalidDataException("submittedAt is required.");
        }

        if (order.Lines is not { Count: > 0 })
        {
            throw new InvalidDataException("Kitchen Order must contain at least one item.");
        }

        var lines = new List<EscPosTextLine>
        {
            new("ใบสั่งครัว", FontSize: 36, Bold: true, Alignment: EscPosTextAlignment.Center),
            new($"โต๊ะ {order.TableId}", FontSize: 42, Bold: true, Alignment: EscPosTextAlignment.Center),
            new($"สถานี {order.ProductionStationId}", $"{order.SubmittedAt:HH:mm:ss}", 25),
            new($"Batch: {order.OrderBatchId}", FontSize: 22),
            new(new string('=', 34), Alignment: EscPosTextAlignment.Center)
        };
        foreach (var item in order.Lines.OrderBy(item => item.LineSequence))
        {
            Validate(item);
            lines.Add(new EscPosTextLine(
                $"{item.Quantity} x {item.DisplayName}",
                Fulfillment(item.FulfillmentType),
                31,
                true,
                TopSpacingDots: 8));
            foreach (var option in item.Options ?? [])
            {
                Require(option.OptionGroupId, "optionGroupId");
                Require(option.DisplayName, "option displayName");
                Positive(option.QuantityPerItem, "option quantityPerItem");
                lines.Add(new EscPosTextLine(
                    $"  + {option.DisplayName} x{option.QuantityPerItem}",
                    FontSize: 26));
            }

            if (!string.IsNullOrWhiteSpace(item.Comment))
            {
                lines.Add(new EscPosTextLine($"  หมายเหตุ: {item.Comment.Trim()}", FontSize: 27, Bold: true));
            }
        }

        return lines;
    }

    private static byte[] Encode(
        MonochromeRaster raster,
        string? cutterCommandHex,
        CancellationToken cancellationToken)
    {
        var rowBytes = (raster.WidthDots + 7) / 8;
        if (rowBytes > ushort.MaxValue)
        {
            throw new InvalidDataException("The raster is too wide for the ESC/POS command.");
        }

        var source = raster.PackedRows.Span;
        using var output = new MemoryStream();
        output.Write(InitializePrinter);
        var rowOffset = 0;
        while (rowOffset < raster.HeightDots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = Math.Min(MaximumRasterChunkRows, raster.HeightDots - rowOffset);
            output.WriteByte(0x1D);
            output.WriteByte(0x76);
            output.WriteByte(0x30);
            output.WriteByte(0x00);
            output.WriteByte((byte)(rowBytes & 0xFF));
            output.WriteByte((byte)((rowBytes >> 8) & 0xFF));
            output.WriteByte((byte)(rows & 0xFF));
            output.WriteByte((byte)((rows >> 8) & 0xFF));
            output.Write(source.Slice(rowOffset * rowBytes, rows * rowBytes));
            rowOffset += rows;
        }

        output.Write(FeedThreeLines);
        if (cutterCommandHex is not null)
        {
            output.Write(Convert.FromHexString(cutterCommandHex));
        }

        return output.ToArray();
    }

    private static void ValidateProfile(PrinterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Protocol != PrinterProtocol.RawTcpEscPos)
        {
            throw new InvalidOperationException("The ESC/POS renderer requires a RAW TCP ESC/POS profile.");
        }

        if (profile.RenderMode != PrinterRenderMode.BitmapRaster ||
            profile.ThaiEncodingMode != PrinterThaiEncodingMode.UnicodeRaster)
        {
            throw new InvalidOperationException("The printer profile must use Unicode bitmap raster rendering.");
        }
    }

    private static void ValidateSource(PrintSourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException("Unsupported source document schema version.");
        }

        if (string.IsNullOrWhiteSpace(document.ContentJson))
        {
            throw new InvalidDataException("Source document JSON is required.");
        }

        var bytes = Encoding.UTF8.GetBytes(document.ContentJson);
        if (bytes.Length > MaximumJsonBytes)
        {
            throw new InvalidDataException("Source document JSON exceeds the maximum size.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, document.ContentSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Source document SHA-256 does not match its JSON content.");
        }
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidDataException("The print document JSON cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The print document JSON is invalid.", exception);
        }
    }

    private static void Validate(BillPreviewItem item)
    {
        Require(item.OrderLineId, "orderLineId");
        Require(item.OrderBatchId, "orderBatchId");
        Require(item.SellableItemId, "sellableItemId");
        Require(item.DisplayName, "displayName");
        Require(item.FulfillmentType, "fulfillmentType");
        Positive(item.Quantity, "quantity");
        NonNegative(item.UnitPrice, "unitPrice");
        NonNegative(item.GrossAmount, "grossAmount");
        NonNegative(item.NetAmount, "netAmount");
        Validate(item.Discount);
    }

    private static void Validate(KitchenOrderItem item)
    {
        Require(item.OrderLineId, "orderLineId");
        Require(item.DisplayName, "displayName");
        Positive(item.LineSequence, "lineSequence");
        Positive(item.Quantity, "quantity");
    }

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{name} is required.")
            : value.Trim();

    private static void Positive(int value, string name)
    {
        if (value < 1)
        {
            throw new InvalidDataException($"{name} must be positive.");
        }
    }

    private static void NonNegative(decimal value, string name)
    {
        if (value < 0)
        {
            throw new InvalidDataException($"{name} cannot be negative.");
        }
    }

    private static void Percentage(decimal value, string name)
    {
        if (value is < 0 or > 100)
        {
            throw new InvalidDataException($"{name} must be between 0 and 100.");
        }
    }

    private static void Validate(BillDiscount? discount)
    {
        if (discount is null)
        {
            return;
        }

        Require(discount.Kind, "discount kind");
        NonNegative(discount.RequestedValue, "discount requestedValue");
        NonNegative(discount.RequestedAmount, "discount requestedAmount");
        NonNegative(discount.AppliedAmount, "discount appliedAmount");
    }

    private static string Fulfillment(string value) => value switch
    {
        "DineIn" => "ทานที่ร้าน",
        "Takeaway" => "กลับบ้าน",
        _ => throw new InvalidDataException("Unsupported fulfillment type.")
    };

    private static string Fulfillment(int value) => value switch
    {
        0 => "ทานที่ร้าน",
        1 => "กลับบ้าน",
        _ => throw new InvalidDataException("Unsupported fulfillment type.")
    };

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string SignedMoney(decimal value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    private static string Percent(decimal value) => $"{value.ToString("0.##", CultureInfo.InvariantCulture)}%";

    private sealed record BillPreviewDocument(
        string? BranchId,
        string? RequestId,
        string? TableSessionId,
        string? TableId,
        DateTimeOffset CapturedAt,
        string? MasterDataVersion,
        string? CalculationPolicyVersion,
        decimal VatRate,
        decimal ServiceChargeRate,
        IReadOnlyList<BillPreviewItem>? Items,
        decimal ItemGrossAmount,
        decimal LineDiscountAmount,
        decimal ItemNetAmount,
        BillDiscount? BillDiscount,
        decimal ServiceChargeBase,
        decimal ServiceChargeAmount,
        decimal TotalBeforeRounding,
        decimal RoundingAdjustment,
        decimal NetTotal,
        decimal VatInclusiveAmount,
        decimal AmountBeforeVat);

    private sealed record BillPreviewItem(
        string? OrderLineId,
        string? OrderBatchId,
        string? SellableItemId,
        string? DisplayName,
        int Quantity,
        decimal UnitPrice,
        string? FulfillmentType,
        string? Comment,
        IReadOnlyList<BillOption>? Options,
        decimal GrossAmount,
        BillDiscount? Discount,
        decimal NetAmount);

    private sealed record BillOption(
        string? OptionId,
        string? DisplayName,
        int QuantityPerItem,
        decimal UnitPrice);

    private sealed record BillDiscount(
        string? Kind,
        decimal RequestedValue,
        decimal RequestedAmount,
        decimal AppliedAmount);

    private sealed record KitchenOrderDocument(
        int SchemaVersion,
        string? OrderBatchId,
        string? TableSessionId,
        string? TableId,
        string? ProductionStationId,
        DateTimeOffset SubmittedAt,
        IReadOnlyList<KitchenOrderItem>? Lines);

    private sealed record KitchenOrderItem(
        string? OrderLineId,
        int LineSequence,
        string? DisplayName,
        int Quantity,
        int FulfillmentType,
        string? Comment,
        IReadOnlyList<KitchenOrderOption>? Options);

    private sealed record KitchenOrderOption(
        string? OptionGroupId,
        string? DisplayName,
        int QuantityPerItem);
}
