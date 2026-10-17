using System.Globalization;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Printing;

public sealed class PrintRuntimeOptions
{
    public const string SectionName = "PrintRuntime";

    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class PrintRuntimeOptionsValidator : IValidateOptions<PrintRuntimeOptions>
{
    private readonly IOptions<PrintSpoolPollingOptions> _pollingOptions;
    private readonly IBranchDatabaseReadinessProbe _databaseReadiness;

    public PrintRuntimeOptionsValidator(
        IOptions<PrintSpoolPollingOptions> pollingOptions,
        IBranchDatabaseReadinessProbe databaseReadiness)
    {
        _pollingOptions = pollingOptions ?? throw new ArgumentNullException(nameof(pollingOptions));
        _databaseReadiness = databaseReadiness ??
            throw new ArgumentNullException(nameof(databaseReadiness));
    }

    public ValidateOptionsResult Validate(string? name, PrintRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_pollingOptions.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        ValidateDatabase(options.DatabasePath, errors);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private void ValidateDatabase(string? configuredPath, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            errors.Add("DatabasePath is required when print spool polling is enabled.");
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

        var readiness = _databaseReadiness.Check(path);
        if (readiness.IsReady)
        {
            return;
        }

        if (string.Equals(readiness.ErrorCode, "SchemaVersionMismatch", StringComparison.Ordinal))
        {
            errors.Add(
                $"Branch database schema version must be {_databaseReadiness.ExpectedSchemaVersion}; " +
                $"actual version is " +
                $"{readiness.ActualSchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.");
            return;
        }

        if (string.Equals(readiness.ErrorCode, "RequiredPrintTablesMissing", StringComparison.Ordinal))
        {
            errors.Add(
                $"Branch database is missing required print tables: " +
                $"{string.Join(", ", readiness.MissingTables)}.");
            return;
        }

        if (string.Equals(readiness.ErrorCode, "UncheckpointedWalPresent", StringComparison.Ordinal))
        {
            errors.Add(
                "Branch database has an uncheckpointed WAL file and is not ready for immutable validation.");
            return;
        }

        errors.Add("Branch database could not be opened read-only for readiness validation.");
    }
}
