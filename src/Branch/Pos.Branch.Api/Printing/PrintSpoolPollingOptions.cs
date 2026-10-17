using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public sealed class PrintSpoolPollingOptions
{
    public const string SectionName = "PrintSpoolPolling";

    public bool Enabled { get; init; }

    public string[] PrinterIds { get; init; } = [];

    public string WorkerIdPrefix { get; init; } = "branch-print";

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan BlockedDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ErrorDelay { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed class PrintSpoolPollingOptionsValidator : IValidateOptions<PrintSpoolPollingOptions>
{
    public ValidateOptionsResult Validate(string? name, PrintSpoolPollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        var printerIds = options.PrinterIds ?? [];
        if (printerIds.Length == 0)
        {
            errors.Add("At least one printer identifier is required when polling is enabled.");
        }
        else
        {
            if (printerIds.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add("Printer identifiers cannot be empty.");
            }

            if (printerIds.Any(value =>
                    !string.IsNullOrWhiteSpace(value) &&
                    !string.Equals(value, value.Trim(), StringComparison.Ordinal)))
            {
                errors.Add("Printer identifiers must already be normalized.");
            }

            var normalized = printerIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();
            if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            {
                errors.Add("Printer identifiers must be unique using ordinal comparison.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.WorkerIdPrefix))
        {
            errors.Add("The worker identifier prefix cannot be empty.");
        }

        ValidatePositive(options.LeaseDuration, nameof(options.LeaseDuration), errors);
        ValidatePositive(options.IdleDelay, nameof(options.IdleDelay), errors);
        ValidatePositive(options.BlockedDelay, nameof(options.BlockedDelay), errors);
        ValidatePositive(options.ErrorDelay, nameof(options.ErrorDelay), errors);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidatePositive(TimeSpan value, string name, ICollection<string> errors)
    {
        if (value <= TimeSpan.Zero)
        {
            errors.Add($"{name} must be greater than zero.");
        }
    }
}
