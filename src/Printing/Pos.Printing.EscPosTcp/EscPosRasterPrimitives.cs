namespace Pos.Printing.EscPosTcp;

public enum EscPosTextAlignment
{
    Left,
    Center,
    Right
}

public sealed record EscPosTextLine(
    string Text,
    string? RightText = null,
    float FontSize = 27,
    bool Bold = false,
    EscPosTextAlignment Alignment = EscPosTextAlignment.Left,
    int TopSpacingDots = 0);

public sealed record MonochromeRaster
{
    private readonly byte[] _packedRows;

    public MonochromeRaster(int widthDots, int heightDots, ReadOnlySpan<byte> packedRows)
    {
        if (widthDots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(widthDots));
        }

        if (heightDots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(heightDots));
        }

        var expectedLength = checked(((widthDots + 7) / 8) * heightDots);
        if (packedRows.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Packed raster length must be {expectedLength} bytes.",
                nameof(packedRows));
        }

        WidthDots = widthDots;
        HeightDots = heightDots;
        _packedRows = packedRows.ToArray();
    }

    public int WidthDots { get; }

    public int HeightDots { get; }

    public ReadOnlyMemory<byte> PackedRows => _packedRows.ToArray();
}

public interface IEscPosDocumentRasterizer
{
    MonochromeRaster Rasterize(
        IReadOnlyList<EscPosTextLine> lines,
        int widthDots,
        CancellationToken cancellationToken);
}
