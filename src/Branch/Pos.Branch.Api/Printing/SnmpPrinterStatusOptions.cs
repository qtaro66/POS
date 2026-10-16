using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Pos.Printing.Snmp;

namespace Pos.Branch.Api.Printing;

public sealed class SnmpPrinterStatusOptions
{
    public const string SectionName = "SnmpPrinterStatus";

    public bool Enabled { get; init; }

    public int Port { get; init; } = SnmpV1UdpTransportOptions.DefaultSnmpPort;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public int MaximumRequestBytes { get; init; } = 4_096;

    public int MaximumResponseBytes { get; init; } = 4_096;

    public Dictionary<string, string> CommunityEnvironmentVariablesByPrinterId { get; init; } =
        new(StringComparer.Ordinal);
}

public sealed partial class SnmpPrinterStatusOptionsValidator :
    IValidateOptions<SnmpPrinterStatusOptions>
{
    public ValidateOptionsResult Validate(string? name, SnmpPrinterStatusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        if (options.Port is < 1 or > 65_535)
        {
            errors.Add("Port must be between 1 and 65535.");
        }

        ValidatePositive(options.ConnectTimeout, nameof(options.ConnectTimeout), errors);
        ValidatePositive(options.ResponseTimeout, nameof(options.ResponseTimeout), errors);
        ValidateDatagramSize(
            options.MaximumRequestBytes,
            nameof(options.MaximumRequestBytes),
            errors);
        ValidateDatagramSize(
            options.MaximumResponseBytes,
            nameof(options.MaximumResponseBytes),
            errors);

        var mappings = options.CommunityEnvironmentVariablesByPrinterId;
        if (mappings is null || mappings.Count == 0)
        {
            errors.Add(
                "At least one printer-to-community-environment-variable mapping is required when SNMP status is enabled.");
        }
        else
        {
            foreach (var (printerId, variableName) in mappings)
            {
                if (string.IsNullOrWhiteSpace(printerId) ||
                    !string.Equals(printerId, printerId.Trim(), StringComparison.Ordinal))
                {
                    errors.Add("SNMP printer identifiers must be non-empty and already normalized.");
                }

                if (string.IsNullOrWhiteSpace(variableName) ||
                    !PortableEnvironmentVariableName().IsMatch(variableName))
                {
                    errors.Add(
                        "SNMP community environment-variable names must use the POS_SNMP_COMMUNITY_ prefix and portable uppercase ASCII names.");
                }
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidatePositive(
        TimeSpan value,
        string name,
        ICollection<string> errors)
    {
        if (value <= TimeSpan.Zero)
        {
            errors.Add($"{name} must be greater than zero.");
        }
    }

    private static void ValidateDatagramSize(
        int value,
        string name,
        ICollection<string> errors)
    {
        if (value is < 1 or > SnmpV1UdpTransportOptions.MaximumUdpPayloadBytes)
        {
            errors.Add(
                $"{name} must be between 1 and {SnmpV1UdpTransportOptions.MaximumUdpPayloadBytes}.");
        }
    }

    [GeneratedRegex("^POS_SNMP_COMMUNITY_[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PortableEnvironmentVariableName();
}
