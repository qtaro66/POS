using System.Globalization;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Pos.Printing.EscPosTcp;

public sealed class SkiaThaiTextRasterizer : IEscPosDocumentRasterizer, IDisposable
{
    private const int HorizontalMarginDots = 12;
    private const int BottomMarginDots = 16;
    private const int MaximumHeightDots = 60_000;
    private readonly SKTypeface _regularTypeface;
    private readonly SKTypeface _boldTypeface;
    private readonly int _threshold;
    private readonly bool _synthesizeBold;
    private bool _disposed;

    public SkiaThaiTextRasterizer(string fontFilePath, int threshold = 180)
        : this(fontFilePath, fontFilePath, threshold, synthesizeBold: true)
    {
    }

    public SkiaThaiTextRasterizer(
        string regularFontFilePath,
        string boldFontFilePath,
        int threshold = 180)
        : this(regularFontFilePath, boldFontFilePath, threshold, synthesizeBold: false)
    {
    }

    private SkiaThaiTextRasterizer(
        string regularFontFilePath,
        string boldFontFilePath,
        int threshold,
        bool synthesizeBold)
    {
        if (string.IsNullOrWhiteSpace(regularFontFilePath))
        {
            throw new ArgumentException("A Thai-capable regular font file is required.", nameof(regularFontFilePath));
        }

        if (string.IsNullOrWhiteSpace(boldFontFilePath))
        {
            throw new ArgumentException("A Thai-capable bold font file is required.", nameof(boldFontFilePath));
        }

        if (!File.Exists(regularFontFilePath))
        {
            throw new FileNotFoundException("The configured regular font file does not exist.", regularFontFilePath);
        }

        if (!File.Exists(boldFontFilePath))
        {
            throw new FileNotFoundException("The configured bold font file does not exist.", boldFontFilePath);
        }

        if (threshold is < 1 or > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }

        var regularTypeface = SKTypeface.FromFile(regularFontFilePath)
            ?? throw new InvalidDataException("SkiaSharp could not load the configured regular font file.");
        try
        {
            _boldTypeface = SKTypeface.FromFile(boldFontFilePath)
                ?? throw new InvalidDataException("SkiaSharp could not load the configured bold font file.");
        }
        catch
        {
            regularTypeface.Dispose();
            throw;
        }

        _regularTypeface = regularTypeface;
        _synthesizeBold = synthesizeBold;
        _threshold = threshold;
    }

    public MonochromeRaster Rasterize(
        IReadOnlyList<EscPosTextLine> lines,
        int widthDots,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(lines);
        if (widthDots <= HorizontalMarginDots * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(widthDots));
        }

        if (lines.Count is < 1 or > 1_000)
        {
            throw new InvalidDataException("The document must contain between 1 and 1,000 text lines.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureGlyphCoverage(lines);
        var laidOut = Layout(lines, widthDots - (HorizontalMarginDots * 2), cancellationToken);
        var height = checked(
            laidOut.Sum(line => line.TopSpacingDots + line.HeightDots) + BottomMarginDots);
        if (height > MaximumHeightDots)
        {
            throw new InvalidDataException("The rendered document exceeds the maximum raster height.");
        }

        using var bitmap = new SKBitmap(
            new SKImageInfo(widthDots, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        using var regularShaper = new SKShaper(_regularTypeface);
        using var boldShaper = new SKShaper(_boldTypeface);
        canvas.Clear(SKColors.White);

        var y = 0f;
        foreach (var line in laidOut)
        {
            cancellationToken.ThrowIfCancellationRequested();
            y += line.TopSpacingDots;
            using var font = CreateFont(line.Source.FontSize, line.Source.Bold);
            var baseline = y - font.Metrics.Ascent;
            var x = line.Source.Alignment switch
            {
                EscPosTextAlignment.Left => HorizontalMarginDots,
                EscPosTextAlignment.Center => widthDots / 2f,
                EscPosTextAlignment.Right => widthDots - HorizontalMarginDots,
                _ => throw new InvalidDataException("Unsupported text alignment.")
            };
            var alignment = line.Source.Alignment switch
            {
                EscPosTextAlignment.Left => SKTextAlign.Left,
                EscPosTextAlignment.Center => SKTextAlign.Center,
                EscPosTextAlignment.Right => SKTextAlign.Right,
                _ => throw new InvalidDataException("Unsupported text alignment.")
            };
            var shaper = line.Source.Bold ? boldShaper : regularShaper;
            canvas.DrawShapedText(shaper, line.Text, x, baseline, alignment, font, paint);
            if (line.RightText is not null)
            {
                canvas.DrawShapedText(
                    shaper,
                    line.RightText,
                    widthDots - HorizontalMarginDots,
                    baseline,
                    SKTextAlign.Right,
                    font,
                    paint);
            }

            y += line.HeightDots;
        }

        canvas.Flush();
        return Pack(bitmap, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _regularTypeface.Dispose();
        _boldTypeface.Dispose();
        _disposed = true;
    }

    private void EnsureGlyphCoverage(IEnumerable<EscPosTextLine> lines)
    {
        foreach (var line in lines)
        {
            using var font = CreateFont(line.FontSize, line.Bold);
            foreach (var text in new[] { line.Text, line.RightText })
            {
                if (!string.IsNullOrEmpty(text) && !font.ContainsGlyphs(text))
                {
                    throw new InvalidDataException(
                        "The configured font does not contain every glyph required by the document.");
                }
            }
        }
    }

    private List<LaidOutLine> Layout(
        IEnumerable<EscPosTextLine> lines,
        int availableWidth,
        CancellationToken cancellationToken)
    {
        var result = new List<LaidOutLine>();
        foreach (var source in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(source);
            using var font = CreateFont(source.FontSize, source.Bold);
            using var shaper = new SKShaper(source.Bold ? _boldTypeface : _regularTypeface);
            var rightWidth = source.RightText is null ? 0 : Measure(shaper, font, source.RightText);
            var textWidth = source.RightText is null
                ? availableWidth
                : Math.Max(1, availableWidth - (int)Math.Ceiling(rightWidth) - 16);
            var wrapped = Wrap(source.Text, textWidth, shaper, font);
            var height = Math.Max(1, (int)Math.Ceiling(font.Metrics.Descent - font.Metrics.Ascent + font.Metrics.Leading));
            for (var index = 0; index < wrapped.Count; index++)
            {
                result.Add(new LaidOutLine(
                    source,
                    wrapped[index],
                    index == 0 ? source.RightText : null,
                    height,
                    index == 0 ? source.TopSpacingDots : 0));
            }
        }

        return result;
    }

    private static IReadOnlyList<string> Wrap(
        string text,
        int maximumWidth,
        SKShaper shaper,
        SKFont font)
    {
        if (text.Length == 0)
        {
            return [string.Empty];
        }

        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        var lines = new List<string>();
        var current = string.Empty;
        foreach (var element in elements)
        {
            var candidate = current + element;
            if (current.Length > 0 && Measure(shaper, font, candidate) > maximumWidth)
            {
                lines.Add(current.TrimEnd());
                current = element.TrimStart();
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.TrimEnd());
        }

        return lines;
    }

    private static float Measure(SKShaper shaper, SKFont font, string text) =>
        shaper.Shape(text, font).Width;

    private SKFont CreateFont(float size, bool bold) => new(bold ? _boldTypeface : _regularTypeface, size)
    {
        Embolden = bold && _synthesizeBold,
        Edging = SKFontEdging.Antialias,
        Hinting = SKFontHinting.Normal
    };

    private MonochromeRaster Pack(SKBitmap bitmap, CancellationToken cancellationToken)
    {
        var rowBytes = (bitmap.Width + 7) / 8;
        var packed = new byte[checked(rowBytes * bitmap.Height)];
        var pixels = bitmap.GetPixelSpan();
        var sourceRowBytes = bitmap.RowBytes;
        for (var y = 0; y < bitmap.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixelOffset = (y * sourceRowBytes) + (x * 4);
                var blue = pixels[pixelOffset];
                var green = pixels[pixelOffset + 1];
                var red = pixels[pixelOffset + 2];
                var luminance = ((299 * red) + (587 * green) + (114 * blue)) / 1_000;
                if (luminance < _threshold)
                {
                    packed[(y * rowBytes) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return new MonochromeRaster(bitmap.Width, bitmap.Height, packed);
    }

    private static void Validate(EscPosTextLine line)
    {
        if (line.Text is null || line.RightText is { Length: 0 })
        {
            throw new InvalidDataException("Text lines cannot contain null or empty right-hand text.");
        }

        if (line.FontSize is < 12 or > 72)
        {
            throw new InvalidDataException("Text size must be between 12 and 72 dots.");
        }

        if (line.TopSpacingDots is < 0 or > 200)
        {
            throw new InvalidDataException("Top spacing must be between 0 and 200 dots.");
        }
    }

    private sealed record LaidOutLine(
        EscPosTextLine Source,
        string Text,
        string? RightText,
        int HeightDots,
        int TopSpacingDots);
}
