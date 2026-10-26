using Microsoft.Extensions.Options;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralCloudIngestionOptions
{
    public const string SectionName = "CentralCloudIngestion";

    public bool Enabled { get; init; }

    public string AuthenticationScheme { get; init; } = string.Empty;
}

public sealed class CentralCloudIngestionOptionsValidator
    : IValidateOptions<CentralCloudIngestionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        CentralCloudIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return IsCanonicalAuthenticationScheme(options.AuthenticationScheme)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "AuthenticationScheme must be a canonical ASCII identifier " +
                "containing only letters, digits, '.', '_', or '-' when " +
                "Central Cloud ingestion is enabled.");
    }

    internal static bool IsCanonicalAuthenticationScheme(string? value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= 128 &&
        IsAsciiLetterOrDigit(value[0]) &&
        value.All(character =>
            IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '-');

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or
            >= '0' and <= '9';
}
