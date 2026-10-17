using System.Globalization;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Commands;

public sealed class BranchCommandStatusQueryOptions
{
    public const string SectionName = "BranchCommandStatusQuery";

    public bool Enabled { get; init; }

    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class BranchCommandStatusQueryOptionsValidator :
    IValidateOptions<BranchCommandStatusQueryOptions>
{
    private readonly IBranchCommandStatusReadinessProbe _readiness;

    public BranchCommandStatusQueryOptionsValidator(
        IBranchCommandStatusReadinessProbe readiness)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public ValidateOptionsResult Validate(
        string? name,
        BranchCommandStatusQueryOptions options)
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
                "DatabasePath is required when Branch command status queries are enabled.");
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
            "CommandReceiptsMissing",
            StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Branch database is missing the command_receipts table.");
        }

        return ValidateOptionsResult.Fail(
            "Branch database could not be opened read-only for command status validation.");
    }
}
