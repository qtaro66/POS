using System.Globalization;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudDataSentStatusOptions
{
    public const string SectionName = "BranchCloudDataSentStatus";

    public bool Enabled { get; init; }

    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class BranchCloudDataSentStatusOptionsValidator :
    IValidateOptions<BranchCloudDataSentStatusOptions>
{
    private readonly IBranchCloudDataSentStatusReadinessProbe _readiness;

    public BranchCloudDataSentStatusOptionsValidator(
        IBranchCloudDataSentStatusReadinessProbe readiness)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public ValidateOptionsResult Validate(
        string? name,
        BranchCloudDataSentStatusOptions options)
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
                "DatabasePath is required when Branch Cloud data-sent status is enabled.");
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
            "CloudSyncOutboxMissing",
            StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Branch database is missing the cloud_sync_outbox table.");
        }

        if (string.Equals(
            readiness.ErrorCode,
            "CloudDataSentColumnsMissing",
            StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Branch database cloud_sync_outbox is missing required columns: " +
                $"{string.Join(", ", readiness.MissingColumns)}.");
        }

        return ValidateOptionsResult.Fail(
            "Branch database could not be opened read-only for Cloud data-sent status validation.");
    }
}
