using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudReplayReadinessOptionsValidator(
    IBranchCloudReplayReadinessProbe probe) : IValidateOptions<BranchCloudOutboxDeliveryOptions>
{
    private readonly IBranchCloudReplayReadinessProbe _probe =
        probe ?? throw new ArgumentNullException(nameof(probe));

    public ValidateOptionsResult Validate(string? name, BranchCloudOutboxDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }
        var path = options.DatabasePath?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return ValidateOptionsResult.Fail("Cloud replay readiness requires an absolute existing Branch database path.");
        }
        var result = _probe.Check(path);
        return result.IsReady
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Cloud replay database is not ready: {result.ErrorCode ?? "Unknown"}; " +
                $"expected schema {_probe.ExpectedSchemaVersion}; " +
                $"missing tables: {string.Join(", ", result.MissingTables)}; " +
                $"missing columns: {string.Join("; ", result.MissingColumns.Select(pair => pair.Key + ": " + string.Join(", ", pair.Value)))}.");
    }
}
