using Microsoft.Extensions.Options;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudReplayReadinessOptionsValidatorTests
{
    [Fact]
    public void Disabled_delivery_does_not_probe()
    {
        var probe = new RecordingProbe();
        Assert.True(new BranchCloudReplayReadinessOptionsValidator(probe)
            .Validate(null, new()).Succeeded);
        Assert.Null(probe.Path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Existing_database_requires_successful_replay_probe(bool ready)
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            var probe = new RecordingProbe { Ready = ready };
            var result = new BranchCloudReplayReadinessOptionsValidator(probe)
                .Validate(null, new() { Enabled = true, DatabasePath = path });
            Assert.Equal(ready, result.Succeeded);
            Assert.Equal(path, probe.Path);
            if (!ready)
            {
                Assert.Contains("ReplayTablesMissing", string.Join(" ", result.Failures!));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.db")]
    public void Invalid_path_fails_without_probe(string path)
    {
        var probe = new RecordingProbe();
        var result = new BranchCloudReplayReadinessOptionsValidator(probe)
            .Validate(null, new() { Enabled = true, DatabasePath = path });
        Assert.True(result.Failed);
        Assert.Null(probe.Path);
    }

    private sealed class RecordingProbe : IBranchCloudReplayReadinessProbe
    {
        public int ExpectedSchemaVersion => 22;
        public bool Ready { get; init; }
        public string? Path { get; private set; }
        public BranchCloudReplayReadinessResult Check(string databasePath)
        {
            Path = databasePath;
            return new(Ready, 22, Ready ? [] : ["cloud_sync_sequence_replay_requests"],
                new Dictionary<string, IReadOnlyList<string>>(), Ready ? null : "ReplayTablesMissing");
        }
    }
}
