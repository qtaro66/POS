using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Printing.Tests;

public sealed class KitchenReprintRendererTests
{
    [Theory]
    [InlineData("\"dineIn\"", "ทานที่ร้าน")]
    [InlineData("\"takeaway\"", "กลับบ้าน")]
    [InlineData("0", "ทานที่ร้าน")]
    [InlineData("1", "กลับบ้าน")]
    public async Task Copy_kind_forces_label_and_preserves_original_line(string fulfillment, string label)
    {
        var raster = new CapturingRasterizer(); var renderer = new EscPosRasterPayloadRenderer(raster);
        var json = Json(fulfillment);
        var copy = await renderer.RenderAsync(new(Document(PrintDocumentKind.KitchenOrderReprint, json), Profile()), default);
        Assert.Equal("สำเนา / พิมพ์ซ้ำ", raster.Lines[0].Text);
        Assert.True(raster.Lines[0].Bold);
        Assert.Contains(raster.Lines, line => line.Text == "ใบสั่งครัว");
        Assert.Contains(raster.Lines, line => line.Text == "โต๊ะ ORIGINAL-TABLE");
        Assert.Contains(raster.Lines, line => line.Text == "สถานี ORIGINAL-KITCHEN");
        Assert.Contains(raster.Lines, line => line.Text == "3 x Original name" && line.RightText == label);
        Assert.Contains(raster.Lines, line => line.Text == "  + Original option x2");
        Assert.Contains(raster.Lines, line => line.Text.Contains("Original note", StringComparison.Ordinal));
        Assert.Equal([0x1B, 0x40], copy.Bytes.Span[..2].ToArray());
        Assert.False(Contains(copy.Bytes.ToArray(), Convert.FromHexString("1B700019FA")));
        await renderer.RenderAsync(new(Document(PrintDocumentKind.KitchenOrder, json), Profile()), default);
        Assert.DoesNotContain(raster.Lines, line => line.Text == "สำเนา / พิมพ์ซ้ำ");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Copy_rejects_zero_or_multiple_lines(int count)
    {
        var source = JsonNode.Parse(Json("\"takeaway\""))!;
        var line = source["lines"]![0]!.DeepClone(); var lines = new JsonArray();
        for (var index = 0; index < count; index++) { lines.Add(line.DeepClone()); }
        source["lines"] = lines;
        var raster = new CapturingRasterizer();
        await Assert.ThrowsAsync<InvalidDataException>(() => new EscPosRasterPayloadRenderer(raster)
            .RenderAsync(new(Document(PrintDocumentKind.KitchenOrderReprint, source.ToJsonString()), Profile()), default));
        Assert.Empty(raster.Lines);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("\"DineIn\"")]
    [InlineData("\" takeaway\"")]
    [InlineData("true")]
    [InlineData("null")]
    public async Task Copy_rejects_unknown_fulfillment_without_guessing(string value)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => new EscPosRasterPayloadRenderer(new CapturingRasterizer())
            .RenderAsync(new(Document(PrintDocumentKind.KitchenOrderReprint, Json(value)), Profile()), default));
    }

    [Fact]
    public async Task Payload_flags_cannot_hide_copy_label_or_change_document_kind()
    {
        var root = JsonNode.Parse(Json("\"dineIn\""))!; root["isReprint"] = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => new EscPosRasterPayloadRenderer(new CapturingRasterizer())
            .RenderAsync(new(Document(PrintDocumentKind.KitchenOrderReprint, root.ToJsonString()), Profile()), default));
        Assert.Equal(0, (int)PrintDocumentKind.BillPreview);
        Assert.Equal(1, (int)PrintDocumentKind.KitchenOrder);
        Assert.Equal(2, (int)PrintDocumentKind.CancellationInspection);
        Assert.Equal(3, (int)PrintDocumentKind.KitchenOrderReprint);
    }

    private static string Json(string fulfillment) => """
        {"schemaVersion":1,"orderBatchId":"ORIGINAL-BATCH","tableSessionId":"ORIGINAL-SESSION",
         "tableId":"ORIGINAL-TABLE","productionStationId":"ORIGINAL-KITCHEN","submittedAt":"2026-11-27T01:00:00Z",
         "lines":[{"orderLineId":"ORIGINAL-LINE","lineSequence":7,"displayName":"Original name","quantity":3,
          "fulfillmentType":FULFILLMENT,"comment":"Original note\nSecond line",
          "options":[{"optionGroupId":null,"displayName":"Original option","quantityPerItem":2}]}]}
        """.Replace("FULFILLMENT", fulfillment, StringComparison.Ordinal);
    private static PrintSourceDocument Document(PrintDocumentKind kind, string json) =>
        new("COPY-SNAPSHOT", kind, 1, json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    private static PrinterProfile Profile() => new("PRINTER", "Kitchen", "BRANCH", "ORIGINAL-KITCHEN", "printer.invalid", 9100,
        PrinterProtocol.RawTcpEscPos, "epson-tm-t82x-m352a-v1", "Epson", "TM-T82X", null, null, 80, 203, 576,
        PrinterRenderMode.BitmapRaster, PrinterThaiEncodingMode.UnicodeRaster, PrinterStatusMode.Unsupported, true,
        "1D5601", "1B700019FA", null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, TimeSpan.FromSeconds(1));
    private static bool Contains(byte[] bytes, byte[] pattern) => bytes.AsSpan().IndexOf(pattern) >= 0;
    private sealed class CapturingRasterizer : IEscPosDocumentRasterizer
    {
        public IReadOnlyList<EscPosTextLine> Lines { get; private set; } = [];
        public MonochromeRaster Rasterize(IReadOnlyList<EscPosTextLine> lines, int widthDots, CancellationToken token)
        { Lines = lines.ToArray(); return new(widthDots, 2, new byte[((widthDots + 7) / 8) * 2]); }
    }
}
