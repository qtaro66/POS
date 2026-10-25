using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudOutboxDeliveryOptionsValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "pos-cloud-outbox-options-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Disabled_configuration_needs_no_values_and_skips_probe()
    {
        var probe = ReadyProbe();
        var validator = new BranchCloudOutboxDeliveryOptionsValidator(probe);

        var result = validator.Validate(
            null,
            new BranchCloudOutboxDeliveryOptions());

        Assert.True(result.Succeeded);
        Assert.Null(probe.DatabasePath);
    }

    [Fact]
    public void Enabled_ready_configuration_succeeds()
    {
        var path = ExistingFile();
        var probe = ReadyProbe();
        var validator = new BranchCloudOutboxDeliveryOptionsValidator(probe);

        var result = validator.Validate(null, Enabled(path));

        Assert.True(result.Succeeded);
        Assert.Equal(path, probe.DatabasePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Enabled_configuration_requires_database_path(string path)
    {
        var probe = ReadyProbe();

        var result = new BranchCloudOutboxDeliveryOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, "DatabasePath is required");
        Assert.Null(probe.DatabasePath);
    }

    [Fact]
    public void Enabled_configuration_requires_absolute_existing_database()
    {
        var probe = ReadyProbe();
        var validator = new BranchCloudOutboxDeliveryOptionsValidator(probe);

        var relative = validator.Validate(null, Enabled("branch.db"));
        var missing = validator.Validate(
            null,
            Enabled(Path.Combine(_directory, "missing.db")));

        AssertFailure(relative, "DatabasePath must be an absolute path");
        AssertFailure(missing, "existing Branch database file");
        Assert.Null(probe.DatabasePath);
    }

    [Theory]
    [InlineData("", "WORKER-01", "BranchId cannot be empty")]
    [InlineData(" ", "WORKER-01", "BranchId cannot be empty")]
    [InlineData(" BRANCH-01", "WORKER-01", "BranchId must already be normalized")]
    [InlineData("BRANCH-01", "", "WorkerId cannot be empty")]
    [InlineData("BRANCH-01", "WORKER-01 ", "WorkerId must already be normalized")]
    public void Enabled_configuration_requires_normalized_identities(
        string branchId,
        string workerId,
        string expected)
    {
        var options = Enabled(
            ExistingFile(),
            branchId: branchId,
            workerId: workerId);

        var result = new BranchCloudOutboxDeliveryOptionsValidator(ReadyProbe())
            .Validate(null, options);

        AssertFailure(result, expected);
    }

    [Theory]
    [InlineData("https://cloud.example.test")]
    [InlineData("https://cloud.example.test:8443")]
    [InlineData("https://192.0.2.10")]
    [InlineData("https://[2001:db8::10]:8443")]
    public void Enabled_configuration_accepts_https_service_origin(string serviceOrigin)
    {
        var result = new BranchCloudOutboxDeliveryOptionsValidator(ReadyProbe())
            .Validate(
                null,
                Enabled(ExistingFile(), serviceOrigin: serviceOrigin));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" https://cloud.example.test")]
    [InlineData("http://cloud.example.test")]
    [InlineData("https://user:secret@cloud.example.test")]
    [InlineData("https://cloud.example.test/events")]
    [InlineData("https://cloud.example.test?branch=1")]
    [InlineData("https://cloud.example.test#fragment")]
    [InlineData("cloud.example.test")]
    public void Enabled_configuration_rejects_invalid_service_origin(
        string serviceOrigin)
    {
        var result = new BranchCloudOutboxDeliveryOptionsValidator(ReadyProbe())
            .Validate(
                null,
                Enabled(ExistingFile(), serviceOrigin: serviceOrigin));

        AssertFailure(result, "absolute HTTPS origin");
    }

    [Theory]
    [InlineData("/events")]
    [InlineData("/api/v1/branch-events")]
    [InlineData("/cloud_delivery/v1.0/events~pending")]
    public void Enabled_configuration_accepts_canonical_delivery_path(
        string deliveryPath)
    {
        var result = new BranchCloudOutboxDeliveryOptionsValidator(ReadyProbe())
            .Validate(
                null,
                Enabled(ExistingFile(), deliveryPath: deliveryPath));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("events")]
    [InlineData("//other.example/events")]
    [InlineData("/events/")]
    [InlineData("/events//pending")]
    [InlineData("/events/../admin")]
    [InlineData("/events/./pending")]
    [InlineData("/events%2Fpending")]
    [InlineData("/events?branch=1")]
    [InlineData("/events#fragment")]
    [InlineData("/events pending")]
    public void Enabled_configuration_rejects_invalid_delivery_path(
        string deliveryPath)
    {
        var result = new BranchCloudOutboxDeliveryOptionsValidator(ReadyProbe())
            .Validate(
                null,
                Enabled(ExistingFile(), deliveryPath: deliveryPath));

        AssertFailure(result, "canonical absolute URL path");
    }

    [Theory]
    [InlineData(0, 1, 1, "LeaseDuration")]
    [InlineData(-1, 1, 1, "LeaseDuration")]
    [InlineData(1, 0, 1, "IdleDelay")]
    [InlineData(1, -1, 1, "IdleDelay")]
    [InlineData(1, 1, 0, "RetryDelay")]
    [InlineData(1, 1, -1, "RetryDelay")]
    public void Enabled_configuration_requires_positive_durations(
        int leaseSeconds,
        int idleSeconds,
        int retrySeconds,
        string expected)
    {
        var options = Enabled(
            ExistingFile(),
            leaseDuration: TimeSpan.FromSeconds(leaseSeconds),
            idleDelay: TimeSpan.FromSeconds(idleSeconds),
            retryDelay: TimeSpan.FromSeconds(retrySeconds));

        var result = new BranchCloudOutboxDeliveryOptionsValidator(ReadyProbe())
            .Validate(null, options);

        AssertFailure(result, $"{expected} must be greater than zero");
    }

    [Theory]
    [InlineData("SchemaVersionMismatch", "schema version must be 18; actual version is 17")]
    [InlineData("CloudSyncOutboxMissing", "missing the cloud_sync_outbox table")]
    [InlineData("CloudSyncOutboxColumnsMissing", "required columns: lease_expires_at")]
    [InlineData("CloudDeliveryFailuresMissing", "missing the cloud_sync_delivery_failures table")]
    [InlineData("CloudDeliveryFailureColumnsMissing", "required columns: retry_count_after")]
    [InlineData("DatabaseUnreadable", "could not be opened read-only")]
    public void Readiness_failure_is_reported(string errorCode, string expected)
    {
        var path = ExistingFile();
        var probe = new RecordingProbe(new(
            false,
            errorCode == "SchemaVersionMismatch" ? 17 : 18,
            errorCode != "CloudSyncOutboxMissing",
            errorCode is not ("CloudSyncOutboxMissing" or "CloudDeliveryFailuresMissing"),
            errorCode == "CloudSyncOutboxColumnsMissing" ? ["lease_expires_at"] : [],
            errorCode == "CloudDeliveryFailureColumnsMissing" ? ["retry_count_after"] : [],
            errorCode));

        var result = new BranchCloudOutboxDeliveryOptionsValidator(probe)
            .Validate(null, Enabled(path));

        AssertFailure(result, expected);
    }

    [Fact]
    public void Missing_dependencies_and_options_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudOutboxDeliveryOptionsValidator(null!));
        var validator = new BranchCloudOutboxDeliveryOptionsValidator(ReadyProbe());
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

    private static BranchCloudOutboxDeliveryOptions Enabled(
        string path,
        string branchId = "BRANCH-01",
        string workerId = "WORKER-01",
        string serviceOrigin = "https://cloud.example.test",
        string deliveryPath = "/test/cloud-events",
        TimeSpan? leaseDuration = null,
        TimeSpan? idleDelay = null,
        TimeSpan? retryDelay = null) =>
        new()
        {
            Enabled = true,
            DatabasePath = path,
            BranchId = branchId,
            WorkerId = workerId,
            ServiceOrigin = serviceOrigin,
            DeliveryPath = deliveryPath,
            LeaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30),
            IdleDelay = idleDelay ?? TimeSpan.FromSeconds(1),
            RetryDelay = retryDelay ?? TimeSpan.FromSeconds(5)
        };

    private static RecordingProbe ReadyProbe() =>
        new(new(true, 18, true, true, [], [], null));

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
        BranchCloudOutboxReadinessResult result) :
        IBranchCloudOutboxReadinessProbe
    {
        public int ExpectedSchemaVersion => 18;

        public string? DatabasePath { get; private set; }

        public BranchCloudOutboxReadinessResult Check(string databasePath)
        {
            DatabasePath = databasePath;
            return result;
        }
    }
}
