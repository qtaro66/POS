using Microsoft.Extensions.Options;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeSessionOptionsValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "pos-employee-session-options-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Disabled_configuration_requires_no_database_or_lifetime()
    {
        var readiness = new StubReadinessProbe(Ready());
        var validator = new BranchEmployeeSessionOptionsValidator(readiness);

        var result = validator.Validate(null, new BranchEmployeeSessionOptions());

        Assert.True(result.Succeeded);
        Assert.Equal(0, readiness.CallCount);
    }

    [Fact]
    public void Enabled_configuration_accepts_explicit_ready_storage_and_lifetime()
    {
        var path = CreateDatabasePlaceholder();
        var readiness = new StubReadinessProbe(Ready());
        var validator = new BranchEmployeeSessionOptionsValidator(readiness);

        var result = validator.Validate(null, Enabled(path));

        Assert.True(result.Succeeded);
        Assert.Equal(path, readiness.DatabasePath);
        Assert.Equal(1, readiness.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Enabled_configuration_requires_database_path(string path)
    {
        var readiness = new StubReadinessProbe(Ready());
        var validator = new BranchEmployeeSessionOptionsValidator(readiness);

        var result = validator.Validate(null, Enabled(path));

        AssertFailure(result, "DatabasePath is required");
        Assert.Equal(0, readiness.CallCount);
    }

    [Fact]
    public void Enabled_configuration_requires_absolute_existing_database()
    {
        var readiness = new StubReadinessProbe(Ready());
        var validator = new BranchEmployeeSessionOptionsValidator(readiness);

        var relative = validator.Validate(null, Enabled("branch.db"));
        var missing = validator.Validate(
            null,
            Enabled(Path.Combine(_directory, "missing.db")));

        AssertFailure(relative, "absolute path");
        AssertFailure(missing, "existing Branch database file");
        Assert.Equal(0, readiness.CallCount);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void Enabled_configuration_requires_positive_lifetime(string lifetime)
    {
        var path = CreateDatabasePlaceholder();
        var readiness = new StubReadinessProbe(Ready());
        var validator = new BranchEmployeeSessionOptionsValidator(readiness);
        var options = Enabled(path, TimeSpan.Parse(lifetime));

        var result = validator.Validate(null, options);

        AssertFailure(result, "Lifetime must be greater than zero");
        Assert.Equal(0, readiness.CallCount);
    }

    [Fact]
    public void Schema_version_mismatch_is_reported_with_expected_and_actual_versions()
    {
        var path = CreateDatabasePlaceholder();
        var readiness = new StubReadinessProbe(new(
            false,
            12,
            [],
            "SchemaVersionMismatch"));
        var validator = new BranchEmployeeSessionOptionsValidator(readiness);

        var result = validator.Validate(null, Enabled(path));

        AssertFailure(result, "schema version must be 13; actual version is 12");
    }

    [Theory]
    [InlineData("EmployeeSessionTablesMissing", "missing required employee session tables")]
    [InlineData("DatabaseUnreadable", "could not be opened read-only")]
    public void Storage_readiness_failure_is_reported(
        string errorCode,
        string expectedMessage)
    {
        var path = CreateDatabasePlaceholder();
        var readiness = new StubReadinessProbe(new(
            false,
            13,
            ["branch_employee_sessions"],
            errorCode));
        var validator = new BranchEmployeeSessionOptionsValidator(readiness);

        var result = validator.Validate(null, Enabled(path));

        AssertFailure(result, expectedMessage);
    }

    [Fact]
    public void Null_options_are_rejected()
    {
        var validator = new BranchEmployeeSessionOptionsValidator(
            new StubReadinessProbe(Ready()));

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null, null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static BranchEmployeeSessionOptions Enabled(
        string path,
        TimeSpan? lifetime = null) =>
        new()
        {
            Enabled = true,
            DatabasePath = path,
            Lifetime = lifetime ?? TimeSpan.FromHours(8)
        };

    private static BranchEmployeeSessionReadinessResult Ready() =>
        new(true, 13, [], null);

    private string CreateDatabasePlaceholder()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "branch.db");
        File.WriteAllBytes(path, []);
        return path;
    }

    private static void AssertFailure(
        ValidateOptionsResult result,
        string expectedMessage)
    {
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(expectedMessage, StringComparison.Ordinal));
    }

    private sealed class StubReadinessProbe(
        BranchEmployeeSessionReadinessResult result) :
        IBranchEmployeeSessionReadinessProbe
    {
        public int ExpectedSchemaVersion => 13;

        public int CallCount { get; private set; }

        public string? DatabasePath { get; private set; }

        public BranchEmployeeSessionReadinessResult Check(string databasePath)
        {
            CallCount++;
            DatabasePath = databasePath;
            return result;
        }
    }
}
