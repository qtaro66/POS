using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public sealed class EscPosRasterRenderingOptions
{
    public const string SectionName = "EscPosRasterRendering";

    public string FontFilePath { get; init; } = string.Empty;

    public string BoldFontFilePath { get; init; } = string.Empty;

    public int MonochromeThreshold { get; init; } = 180;
}

public sealed class EscPosRasterRenderingOptionsValidator :
    IValidateOptions<EscPosRasterRenderingOptions>
{
    private readonly IOptions<PrintSpoolPollingOptions> _pollingOptions;

    public EscPosRasterRenderingOptionsValidator(
        IOptions<PrintSpoolPollingOptions> pollingOptions)
    {
        _pollingOptions = pollingOptions ?? throw new ArgumentNullException(nameof(pollingOptions));
    }

    public ValidateOptionsResult Validate(string? name, EscPosRasterRenderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_pollingOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        ValidateFont(options.FontFilePath, nameof(options.FontFilePath), errors);
        ValidateFont(options.BoldFontFilePath, nameof(options.BoldFontFilePath), errors);
        if (options.MonochromeThreshold is < 1 or > 254)
        {
            errors.Add("MonochromeThreshold must be between 1 and 254.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateFont(
        string? configuredPath,
        string optionName,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            errors.Add($"{optionName} is required when print spool polling is enabled.");
            return;
        }

        var path = configuredPath.Trim();
        if (!Path.IsPathFullyQualified(path))
        {
            errors.Add($"{optionName} must be an absolute path.");
            return;
        }

        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{optionName} must reference a .ttf or .otf font file.");
        }

        if (!File.Exists(path))
        {
            errors.Add($"{optionName} does not reference an existing file.");
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!stream.CanRead)
            {
                errors.Add($"{optionName} is not readable.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errors.Add($"{optionName} is not readable: {exception.GetType().Name}.");
        }
    }
}
