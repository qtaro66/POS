using System.Globalization;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Security;

public sealed class BranchEmployeeSessionOptions
{
    public const string SectionName = "BranchEmployeeSessions";

    public bool Enabled { get; init; }

    public string DatabasePath { get; init; } = string.Empty;

    public TimeSpan Lifetime { get; init; }
}

public sealed class BranchEmployeeSessionOptionsValidator :
    IValidateOptions<BranchEmployeeSessionOptions>
{
    private readonly IBranchEmployeeSessionReadinessProbe _readiness;

    public BranchEmployeeSessionOptionsValidator(
        IBranchEmployeeSessionReadinessProbe readiness)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public ValidateOptionsResult Validate(
        string? name,
        BranchEmployeeSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        var configuredPath = options.DatabasePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            errors.Add(
                "DatabasePath is required when Branch employee sessions are enabled.");
        }
        else if (!Path.IsPathFullyQualified(configuredPath.Trim()))
        {
            errors.Add("DatabasePath must be an absolute path.");
        }
        else if (!File.Exists(configuredPath.Trim()))
        {
            errors.Add(
                "DatabasePath must reference an existing Branch database file.");
        }

        if (options.Lifetime <= TimeSpan.Zero)
        {
            errors.Add("Lifetime must be greater than zero.");
        }

        if (errors.Count > 0)
        {
            return ValidateOptionsResult.Fail(errors);
        }

        var readiness = _readiness.Check(configuredPath.Trim());
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
            "EmployeeSessionTablesMissing",
            StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Branch database is missing required employee session tables.");
        }

        return ValidateOptionsResult.Fail(
            "Branch database could not be opened read-only for employee session validation.");
    }
}
