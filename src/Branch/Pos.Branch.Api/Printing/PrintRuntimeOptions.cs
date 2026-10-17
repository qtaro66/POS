using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public sealed class PrintRuntimeOptions
{
    public const string SectionName = "PrintRuntime";

    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class PrintRuntimeOptionsValidator : IValidateOptions<PrintRuntimeOptions>
{
    private readonly IOptions<PrintSpoolPollingOptions> _pollingOptions;

    public PrintRuntimeOptionsValidator(IOptions<PrintSpoolPollingOptions> pollingOptions)
    {
        _pollingOptions = pollingOptions ?? throw new ArgumentNullException(nameof(pollingOptions));
    }

    public ValidateOptionsResult Validate(string? name, PrintRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_pollingOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        ValidateDatabasePath(options.DatabasePath, errors);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateDatabasePath(string? configuredPath, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            errors.Add("DatabasePath is required when print spool polling is enabled.");
            return;
        }

        var path = configuredPath.Trim();
        if (!Path.IsPathFullyQualified(path))
        {
            errors.Add("DatabasePath must be an absolute path.");
            return;
        }

        if (!File.Exists(path))
        {
            errors.Add("DatabasePath must reference an existing Branch database file.");
        }
    }
}
