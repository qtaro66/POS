using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudDataSentStatusOptionsValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "pos-data-sent-options-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Disabled_configuration_needs_no_database_and_skips_probe()
    {
        var probe = ReadyProbe();
        var validator = new BranchCloudDataSentStatusOptionsValidator(probe);

        var result = validator.Validate(
            null,
            new BranchCloudDataSentStatusOptions
            {
                Enabled = false,
                DatabasePath = string.Empty
            });

        Assert.True(result.Succeeded);
        Assert.Null(probe.DatabasePath);
    }

    [Fact]
    public void Enabled_ready_database_succeeds_with_trimmed_path()
    {
        var path = ExistingFile();
        var probe = ReadyProbe();
        var validator = new BranchCloudDataSentStatusOptionsValidator(probe);

        var result = validator.Validate(
            null,
            Enabled($" {path} "));

        Assert.True(result.Succeeded);
        Assert.Equal(path, probe.DatabasePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Enabled_configuration_requires_database_path(string path)
    {
        var probe = ReadyProbe();
        var result = new BranchCloudDataSentStatusOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, "DatabasePath is required");
        Assert.Null(probe.DatabasePath);
    }

    [Fact]
    public void Enabled_configuration_requires_absolute_path()
    {
        var probe = ReadyProbe();
        var result = new BranchCloudDataSentStatusOptionsValidator(probe)
            .Validate(null, Enabled("branch.db"));

        AssertFailure(result, "DatabasePath must be an absolute path");
        Assert.Null(probe.DatabasePath);
    }

    [Fact]
    public void Enabled_configuration_requires_existing_file()
    {
        var probe = ReadyProbe();
        var path = Path.Combine(_directory, "missing.db");
        var result = new BranchCloudDataSentStatusOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, "existing Branch database file");
        Assert.Null(probe.DatabasePath);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Schema_mismatch_reports_expected_and_actual_versions()
    {
        var path = ExistingFile();
        var probe = new RecordingProbe(new(
            false,
            14,
            true,
            [],
            "SchemaVersionMismatch"));
        var result = new BranchCloudDataSentStatusOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, "must be 16; actual version is 14");
    }

    [Fact]
    public void Missing_outbox_table_is_reported()
    {
        var path = ExistingFile();
        var probe = new RecordingProbe(new(
            false,
            15,
            false,
            ["branch_id", "successfully_sent_at"],
            "CloudSyncOutboxMissing"));
        var result = new BranchCloudDataSentStatusOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, "missing the cloud_sync_outbox table");
    }

    [Fact]
    public void Missing_required_columns_are_reported()
    {
        var path = ExistingFile();
        var probe = new RecordingProbe(new(
            false,
            15,
            true,
            ["successfully_sent_at"],
            "CloudDataSentColumnsMissing"));
        var result = new BranchCloudDataSentStatusOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, "required columns: successfully_sent_at");
    }

    [Fact]
    public void Unreadable_database_fails_closed()
    {
        var path = ExistingFile();
        var probe = new RecordingProbe(new(
            false,
            null,
            false,
            ["branch_id", "successfully_sent_at"],
            "DatabaseUnreadable"));
        var result = new BranchCloudDataSentStatusOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, "could not be opened read-only");
    }

    [Fact]
    public void Missing_dependencies_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudDataSentStatusOptionsValidator(null!));
        var validator = new BranchCloudDataSentStatusOptionsValidator(ReadyProbe());
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null, null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string ExistingFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "branch.db");
        File.WriteAllBytes(path, []);
        return path;
    }

    private static BranchCloudDataSentStatusOptions Enabled(string path) =>
        new()
        {
            Enabled = true,
            DatabasePath = path
        };

    private static RecordingProbe ReadyProbe() =>
        new(new(true, 16, true, [], null));

    private static void AssertFailure(
        ValidateOptionsResult result,
        string expected)
    {
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(expected, StringComparison.Ordinal));
    }

    private sealed class RecordingProbe(
        BranchCloudDataSentStatusReadinessResult result) :
        IBranchCloudDataSentStatusReadinessProbe
    {
        public int ExpectedSchemaVersion => 16;

        public string? DatabasePath { get; private set; }

        public BranchCloudDataSentStatusReadinessResult Check(string databasePath)
        {
            DatabasePath = databasePath;
            return result;
        }
    }
}
