using System.Security.Cryptography;
using System.Text;
using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public sealed class EnvironmentVariableSnmpV1CommunityProvider : ISnmpV1CommunityProvider
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly IReadOnlyDictionary<string, string> _variableNamesByPrinterId;
    private readonly ISnmpV1EnvironmentVariableSource _environment;

    public EnvironmentVariableSnmpV1CommunityProvider(
        IReadOnlyDictionary<string, string> variableNamesByPrinterId)
        : this(variableNamesByPrinterId, new SystemSnmpV1EnvironmentVariableSource())
    {
    }

    internal EnvironmentVariableSnmpV1CommunityProvider(
        IReadOnlyDictionary<string, string> variableNamesByPrinterId,
        ISnmpV1EnvironmentVariableSource environment)
    {
        ArgumentNullException.ThrowIfNull(variableNamesByPrinterId);
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (printerId, variableName) in variableNamesByPrinterId)
        {
            if (string.IsNullOrWhiteSpace(printerId) ||
                !string.Equals(printerId, printerId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Printer identifiers must be non-empty and already normalized.",
                    nameof(variableNamesByPrinterId));
            }

            if (string.IsNullOrWhiteSpace(variableName) ||
                !string.Equals(variableName, variableName.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Environment-variable names must be non-empty and already normalized.",
                    nameof(variableNamesByPrinterId));
            }

            copy.Add(printerId, variableName);
        }

        _variableNamesByPrinterId = copy;
    }

    public ValueTask<SnmpV1CommunitySecret> GetCommunityAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_variableNamesByPrinterId.TryGetValue(profile.PrinterId, out var variableName))
        {
            throw new InvalidOperationException(
                "An SNMP community environment-variable mapping is not configured for this printer.");
        }

        var value = _environment.Get(variableName);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                "The configured SNMP community environment variable is unavailable or empty.");
        }

        var bytes = StrictUtf8.GetBytes(value);
        try
        {
            return ValueTask.FromResult(new SnmpV1CommunitySecret(bytes, takeOwnership: true));
        }
        catch
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }
}

internal interface ISnmpV1EnvironmentVariableSource
{
    string? Get(string name);
}

internal sealed class SystemSnmpV1EnvironmentVariableSource : ISnmpV1EnvironmentVariableSource
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}
