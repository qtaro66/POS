using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public sealed class PrintSpoolRetentionOptions
{
    public const string SectionName = "PrintSpoolRetention";

    public bool Enabled { get; init; }

    public TimeSpan? TerminalPayloadRetention { get; init; }

    public long? MaximumTotalBytes { get; init; }

    public int? MaximumDeletesPerRun { get; init; }

    public TimeSpan? RunInterval { get; init; }
}

public sealed class PrintSpoolRetentionOptionsValidator :
    IValidateOptions<PrintSpoolRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, PrintSpoolRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        ValidatePositive(
            options.TerminalPayloadRetention,
            nameof(options.TerminalPayloadRetention),
            errors);
        if (options.MaximumTotalBytes is null or <= 0)
        {
            errors.Add($"{nameof(options.MaximumTotalBytes)} must be greater than zero.");
        }

        if (options.MaximumDeletesPerRun is null or <= 0)
        {
            errors.Add($"{nameof(options.MaximumDeletesPerRun)} must be greater than zero.");
        }

        ValidatePositive(options.RunInterval, nameof(options.RunInterval), errors);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidatePositive(
        TimeSpan? value,
        string name,
        ICollection<string> errors)
    {
        if (value is null || value <= TimeSpan.Zero)
        {
            errors.Add($"{name} must be greater than zero.");
        }
    }
}
