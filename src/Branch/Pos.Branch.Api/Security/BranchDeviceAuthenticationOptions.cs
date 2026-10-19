using System.Globalization;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Security;

public sealed class BranchDeviceAuthenticationOptions
{
    public const string SectionName = "BranchDeviceAuthentication";

    public bool Enabled { get; init; }

    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class BranchDeviceAuthenticationOptionsValidator :
    IValidateOptions<BranchDeviceAuthenticationOptions>
{
    private readonly IBranchDeviceCredentialReadinessProbe _readiness;

    public BranchDeviceAuthenticationOptionsValidator(
        IBranchDeviceCredentialReadinessProbe readiness)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public ValidateOptionsResult Validate(
        string? name,
        BranchDeviceAuthenticationOptions options)
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
                "DatabasePath is required when Branch device authentication is enabled.");
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
            "DeviceCredentialTablesMissing",
            StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Branch database is missing required device credential tables.");
        }

        return ValidateOptionsResult.Fail(
            "Branch database could not be opened read-only for device credential validation.");
    }
}
