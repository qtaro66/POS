using System.Globalization;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Security;

public sealed class BranchPairingOptions
{
    public const string SectionName = "BranchPairing";

    public bool Enabled { get; init; }

    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class BranchPairingOptionsValidator :
    IValidateOptions<BranchPairingOptions>
{
    private readonly IBranchPairingReadinessProbe _readiness;

    public BranchPairingOptionsValidator(IBranchPairingReadinessProbe readiness)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public ValidateOptionsResult Validate(string? name, BranchPairingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var configuredPath = options.DatabasePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return ValidateOptionsResult.Fail(
                "DatabasePath is required when Branch pairing is enabled.");
        }

        var path = configuredPath.Trim();
        if (!Path.IsPathFullyQualified(path))
        {
            return ValidateOptionsResult.Fail("DatabasePath must be an absolute path.");
        }

        if (!File.Exists(path))
        {
            return ValidateOptionsResult.Fail(
                "DatabasePath must reference an existing Branch database file.");
        }

        var readiness = _readiness.Check(path);
        if (readiness.IsReady)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.Equals(
            readiness.ErrorCode,
            "SchemaVersionMismatch",
            StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Branch database schema version must be {_readiness.ExpectedSchemaVersion}; " +
                $"actual version is " +
                $"{readiness.ActualSchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.");
        }

        if (string.Equals(
            readiness.ErrorCode,
            "PairingTablesMissing",
            StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Branch database is missing required pairing tables.");
        }

        return ValidateOptionsResult.Fail(
            "Branch database could not be opened read-only for pairing validation.");
    }
}
