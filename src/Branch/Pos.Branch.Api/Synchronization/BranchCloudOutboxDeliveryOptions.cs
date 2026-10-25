using System.Globalization;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudOutboxDeliveryOptions
{
    public const string SectionName = "BranchCloudOutboxDelivery";

    public bool Enabled { get; init; }

    public string DatabasePath { get; init; } = string.Empty;

    public string BranchId { get; init; } = string.Empty;

    public string ServiceOrigin { get; init; } = string.Empty;

    public string DeliveryPath { get; init; } = string.Empty;

    public string WorkerId { get; init; } = "branch-cloud-outbox";

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed class BranchCloudOutboxDeliveryOptionsValidator :
    IValidateOptions<BranchCloudOutboxDeliveryOptions>
{
    private readonly IBranchCloudOutboxReadinessProbe _readiness;

    public BranchCloudOutboxDeliveryOptionsValidator(
        IBranchCloudOutboxReadinessProbe readiness)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public ValidateOptionsResult Validate(
        string? name,
        BranchCloudOutboxDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        ValidateIdentifier(options.BranchId, nameof(options.BranchId), errors);
        ValidateIdentifier(options.WorkerId, nameof(options.WorkerId), errors);
        ValidateDeliveryEndpoint(
            options.ServiceOrigin,
            options.DeliveryPath,
            errors);
        ValidatePositive(options.LeaseDuration, nameof(options.LeaseDuration), errors);
        ValidatePositive(options.IdleDelay, nameof(options.IdleDelay), errors);
        ValidatePositive(options.RetryDelay, nameof(options.RetryDelay), errors);
        ValidateDatabase(options.DatabasePath, errors);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateDeliveryEndpoint(
        string? configuredOrigin,
        string? configuredPath,
        ICollection<string> errors)
    {
        try
        {
            BranchCloudDeliveryEndpoint.Require(
                configuredOrigin ?? string.Empty,
                configuredPath ?? string.Empty,
                nameof(BranchCloudOutboxDeliveryOptions.ServiceOrigin),
                nameof(BranchCloudOutboxDeliveryOptions.DeliveryPath));
        }
        catch (ArgumentException exception)
        {
            errors.Add(exception.Message);
        }
    }

    private void ValidateDatabase(
        string? configuredPath,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            errors.Add(
                "DatabasePath is required when Branch Cloud outbox delivery is enabled.");
            return;
        }

        var path = configuredPath.Trim();
        if (!Path.IsPathFullyQualified(path))
        {
            errors.Add("DatabasePath must be an absolute path.");
            return;
        }

        if (!File.Exists(path))
        {
            errors.Add("DatabasePath must reference an existing Branch database file.");
            return;
        }

        var readiness = _readiness.Check(path);
        if (readiness.IsReady)
        {
            return;
        }

        errors.Add(readiness.ErrorCode switch
        {
            "SchemaVersionMismatch" =>
                $"Branch database schema version must be {_readiness.ExpectedSchemaVersion}; " +
                $"actual version is " +
                $"{readiness.ActualSchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.",
            "CloudSyncOutboxMissing" =>
                "Branch database is missing the cloud_sync_outbox table.",
            "CloudSyncOutboxColumnsMissing" =>
                "Branch database cloud_sync_outbox is missing required columns: " +
                $"{string.Join(", ", readiness.MissingOutboxColumns)}.",
            "CloudDeliveryFailuresMissing" =>
                "Branch database is missing the cloud_sync_delivery_failures table.",
            "CloudDeliveryFailureColumnsMissing" =>
                "Branch database cloud_sync_delivery_failures is missing required columns: " +
                $"{string.Join(", ", readiness.MissingDeliveryFailureColumns)}.",
            _ =>
                "Branch database could not be opened read-only for Cloud outbox delivery validation."
        });
    }

    private static void ValidateIdentifier(
        string? value,
        string name,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} cannot be empty.");
            return;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            errors.Add($"{name} must already be normalized.");
        }
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
}
