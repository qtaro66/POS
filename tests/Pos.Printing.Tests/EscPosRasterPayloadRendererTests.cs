using System.Security.Cryptography;
using System.Text;
using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Printing.Tests;

public sealed class EscPosRasterPayloadRendererTests
{
    [Fact]
    public async Task RenderAsync_BillPreview_ProducesRasterAndCutterWithoutDrawerPulse()
    {
        var rasterizer = new CapturingRasterizer();
        var renderer = new EscPosRasterPayloadRenderer(rasterizer);
        var document = Document(PrintDocumentKind.BillPreview, BillPreviewJson);

        var result = await renderer.RenderAsync(
            new PrintRenderRequest(document, Profile()),
            CancellationToken.None);

        Assert.Equal(document.DocumentId, result.DocumentId);
        Assert.Equal(576, rasterizer.WidthDots);
        Assert.Contains(rasterizer.Lines, line => line.Text == "ใบตรวจสอบรายการ");
        Assert.Contains(rasterizer.Lines, line => line.Text == "ยังไม่ใช่ใบเสร็จ");
        Assert.Contains(rasterizer.Lines, line => line.Text.Contains("ผัดไทย", StringComparison.Ordinal));
        Assert.Contains(rasterizer.Lines, line => line.Text.Contains("ไม่ใส่ถั่ว", StringComparison.Ordinal));
        Assert.Contains(rasterizer.Lines, line => line.Text == "ยอดสุทธิ" && line.RightText == "105.00");

        var bytes = result.Bytes.ToArray();
        Assert.Equal([0x1B, 0x40], bytes[..2]);
        Assert.Equal([0x1D, 0x76, 0x30, 0x00, 0x48, 0x00, 0x02, 0x00], bytes[2..10]);
        Assert.Equal([0x1B, 0x64, 0x03, 0x1D, 0x56, 0x01], bytes[^6..]);
        Assert.False(Contains(bytes, Convert.FromHexString("1B700019FA")));
        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes("ผัดไทย")));
    }

    [Fact]
    public async Task RenderAsync_KitchenOrder_PreservesStationFulfillmentOptionsAndComment()
    {
        var rasterizer = new CapturingRasterizer();
        var renderer = new EscPosRasterPayloadRenderer(rasterizer);

        await renderer.RenderAsync(
            new PrintRenderRequest(Document(PrintDocumentKind.KitchenOrder, KitchenOrderJson), Profile()),
            CancellationToken.None);

        Assert.Contains(rasterizer.Lines, line => line.Text == "ใบสั่งครัว");
        Assert.Contains(rasterizer.Lines, line => line.Text == "โต๊ะ A04");
        Assert.Contains(rasterizer.Lines, line => line.Text == "สถานี KITCHEN-01");
        Assert.Contains(rasterizer.Lines, line =>
            line.Text == "2 x ข้าวผัดกุ้ง" && line.RightText == "กลับบ้าน");
        Assert.Contains(rasterizer.Lines, line => line.Text.Contains("ไข่ดาว", StringComparison.Ordinal));
        Assert.Contains(rasterizer.Lines, line => line.Text.Contains("ไม่ใส่ต้นหอม", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenderAsync_SameInput_IsDeterministic()
    {
        var renderer = new EscPosRasterPayloadRenderer(new CapturingRasterizer());
        var request = new PrintRenderRequest(
            Document(PrintDocumentKind.KitchenOrder, KitchenOrderJson),
            Profile());

        var first = await renderer.RenderAsync(request, CancellationToken.None);
        var second = await renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.Bytes.ToArray(), second.Bytes.ToArray());
    }

    [Fact]
    public async Task RenderAsync_LargeRaster_IsSplitIntoBoundedCommands()
    {
        var renderer = new EscPosRasterPayloadRenderer(new CapturingRasterizer(heightDots: 2_050));

        var result = await renderer.RenderAsync(
            new PrintRenderRequest(Document(PrintDocumentKind.KitchenOrder, KitchenOrderJson), Profile()),
            CancellationToken.None);

        var bytes = result.Bytes.ToArray();
        var rowBytes = 72;
        var secondHeader = 2 + 8 + (rowBytes * 2_048);
        Assert.Equal([0x1D, 0x76, 0x30, 0x00, 0x48, 0x00, 0x02, 0x00], bytes[secondHeader..(secondHeader + 8)]);
    }

    [Fact]
    public async Task RenderAsync_HashMismatch_FailsBeforeRasterizing()
    {
        var rasterizer = new CapturingRasterizer();
        var renderer = new EscPosRasterPayloadRenderer(rasterizer);
        var document = Document(PrintDocumentKind.BillPreview, BillPreviewJson) with
        {
            ContentSha256 = new string('0', 64)
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => renderer.RenderAsync(
            new PrintRenderRequest(document, Profile()),
            CancellationToken.None));
        Assert.Empty(rasterizer.Lines);
    }

    [Fact]
    public async Task RenderAsync_UnknownJsonProperty_IsRejected()
    {
        var renderer = new EscPosRasterPayloadRenderer(new CapturingRasterizer());
        var json = KitchenOrderJson.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1, \"unexpected\": true,",
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidDataException>(() => renderer.RenderAsync(
            new PrintRenderRequest(Document(PrintDocumentKind.KitchenOrder, json), Profile()),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(PrinterProtocol.Ipp, PrinterRenderMode.BitmapRaster, PrinterThaiEncodingMode.UnicodeRaster)]
    [InlineData(PrinterProtocol.RawTcpEscPos, PrinterRenderMode.NativeTextExperimental, PrinterThaiEncodingMode.UnicodeRaster)]
    [InlineData(PrinterProtocol.RawTcpEscPos, PrinterRenderMode.BitmapRaster, PrinterThaiEncodingMode.NativeCodePageExperimental)]
    public async Task RenderAsync_IncompatibleProfile_IsRejected(
        PrinterProtocol protocol,
        PrinterRenderMode renderMode,
        PrinterThaiEncodingMode encodingMode)
    {
        var renderer = new EscPosRasterPayloadRenderer(new CapturingRasterizer());

        await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.RenderAsync(
            new PrintRenderRequest(
                Document(PrintDocumentKind.KitchenOrder, KitchenOrderJson),
                Profile(protocol, renderMode, encodingMode)),
            CancellationToken.None));
    }

    [Fact]
    public void MonochromeRaster_InvalidPackedLength_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MonochromeRaster(16, 2, [0x00]));
    }

    [Fact]
    public void SkiaThaiTextRasterizer_MissingFont_IsRejected()
    {
        Assert.Throws<FileNotFoundException>(() =>
            new SkiaThaiTextRasterizer(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.ttf")));
    }

    [Fact]
    public void SkiaThaiTextRasterizer_RendersWithAvailableFontWithoutHardwareAccess()
    {
        var fontPath = FindPortableTestFont();
        if (fontPath is null)
        {
            return;
        }

        using var rasterizer = new SkiaThaiTextRasterizer(fontPath);
        var raster = rasterizer.Rasterize(
            [new EscPosTextLine("Raster smoke test 123", FontSize: 24)],
            384,
            CancellationToken.None);

        Assert.Equal(384, raster.WidthDots);
        Assert.True(raster.HeightDots > 0);
        Assert.Contains(raster.PackedRows.ToArray(), value => value != 0);
    }

    [Fact]
    public void SkiaThaiTextRasterizer_RendersThaiWhenAThaiFontIsAvailable()
    {
        var fontPath = FindThaiTestFont();
        if (fontPath is null)
        {
            return;
        }

        using var rasterizer = new SkiaThaiTextRasterizer(fontPath);
        var raster = rasterizer.Rasterize(
            [new EscPosTextLine("ทดสอบภาษาไทย สระ และวรรณยุกต์", FontSize: 28)],
            576,
            CancellationToken.None);

        Assert.Contains(raster.PackedRows.ToArray(), value => value != 0);
    }

    private static PrintSourceDocument Document(PrintDocumentKind kind, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new PrintSourceDocument(
            "DOC-01",
            kind,
            1,
            json,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static PrinterProfile Profile(
        PrinterProtocol protocol = PrinterProtocol.RawTcpEscPos,
        PrinterRenderMode renderMode = PrinterRenderMode.BitmapRaster,
        PrinterThaiEncodingMode encodingMode = PrinterThaiEncodingMode.UnicodeRaster) => new(
            "PRINTER-TEST",
            "Test printer",
            "BRANCH-TEST",
            "KITCHEN-01",
            "printer.invalid",
            9100,
            protocol,
            "epson-tm-t82x-m352a-v1",
            "Epson",
            "TM-T82X",
            "M352A",
            null,
            80,
            203,
            576,
            renderMode,
            encodingMode,
            PrinterStatusMode.Unsupported,
            true,
            "1D5601",
            "1B700019FA",
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            1,
            TimeSpan.FromSeconds(1));

    private static bool Contains(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value.Length > source.Length)
        {
            return false;
        }

        for (var index = 0; index <= source.Length - value.Length; index++)
        {
            if (source.Slice(index, value.Length).SequenceEqual(value))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindPortableTestFont()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(windows) ? null : Path.Combine(windows, "Fonts", "arial.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        };
        return candidates.FirstOrDefault(path => path is not null && File.Exists(path));
    }

    private static string? FindThaiTestFont()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(windows) ? null : Path.Combine(windows, "Fonts", "tahoma.ttf"),
            "/usr/share/fonts/truetype/noto/NotoSansThai-Regular.ttf",
            "/usr/share/fonts/opentype/noto/NotoSansThai-Regular.ttf"
        };
        return candidates.FirstOrDefault(path => path is not null && File.Exists(path));
    }

    private sealed class CapturingRasterizer(int heightDots = 2) : IEscPosDocumentRasterizer
    {
        public IReadOnlyList<EscPosTextLine> Lines { get; private set; } = [];

        public int WidthDots { get; private set; }

        public MonochromeRaster Rasterize(
            IReadOnlyList<EscPosTextLine> lines,
            int widthDots,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lines = lines.ToArray();
            WidthDots = widthDots;
            var rowBytes = (widthDots + 7) / 8;
            var bytes = Enumerable.Repeat((byte)0xA5, rowBytes * heightDots).ToArray();
            return new MonochromeRaster(widthDots, heightDots, bytes);
        }
    }

    private const string BillPreviewJson = """
        {
          "branchId": "BRANCH-TEST",
          "requestId": "REQ-01",
          "tableSessionId": "SESSION-01",
          "tableId": "A04",
          "capturedAt": "2026-10-15T10:32:00+07:00",
          "masterDataVersion": "MASTER-01",
          "calculationPolicyVersion": "CALC-01",
          "vatRate": 7,
          "serviceChargeRate": 0,
          "items": [
            {
              "orderLineId": "LINE-01",
              "orderBatchId": "BATCH-01",
              "sellableItemId": "ITEM-01",
              "displayName": "ผัดไทย",
              "quantity": 1,
              "unitPrice": 105,
              "fulfillmentType": "DineIn",
              "comment": "ไม่ใส่ถั่ว",
              "options": [
                { "optionId": "OPT-01", "displayName": "ไข่ดาว", "quantityPerItem": 1, "unitPrice": 0 }
              ],
              "grossAmount": 105,
              "discount": null,
              "netAmount": 105
            }
          ],
          "itemGrossAmount": 105,
          "lineDiscountAmount": 0,
          "itemNetAmount": 105,
          "billDiscount": null,
          "serviceChargeBase": 105,
          "serviceChargeAmount": 0,
          "totalBeforeRounding": 105,
          "roundingAdjustment": 0,
          "netTotal": 105,
          "vatInclusiveAmount": 6.87,
          "amountBeforeVat": 98.13
        }
        """;

    private const string KitchenOrderJson = """
        {
          "schemaVersion": 1,
          "orderBatchId": "BATCH-01",
          "tableSessionId": "SESSION-01",
          "tableId": "A04",
          "productionStationId": "KITCHEN-01",
          "submittedAt": "2026-10-15T10:32:00+07:00",
          "lines": [
            {
              "orderLineId": "LINE-01",
              "lineSequence": 1,
              "displayName": "ข้าวผัดกุ้ง",
              "quantity": 2,
              "fulfillmentType": 1,
              "comment": "ไม่ใส่ต้นหอม",
              "options": [
                { "optionGroupId": "GROUP-01", "displayName": "ไข่ดาว", "quantityPerItem": 1 }
              ]
            }
          ]
        }
        """;
}
