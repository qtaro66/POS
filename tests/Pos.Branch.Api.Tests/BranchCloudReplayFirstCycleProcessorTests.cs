using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudReplayFirstCycleProcessorTests
{
    [Theory]
    [InlineData(BranchCloudOutboxCycleDisposition.Processed, 0)]
    [InlineData(BranchCloudOutboxCycleDisposition.NoWork, 1)]
    public async Task Scheduler_checks_replay_before_normal_and_processes_at_most_one(
        BranchCloudOutboxCycleDisposition replayResult, int normalCalls)
    {
        var calls = new List<string>();
        var replay = new Processor("replay", calls, replayResult);
        var normal = new Processor("normal", calls, BranchCloudOutboxCycleDisposition.Processed);
        var scheduler = new BranchCloudReplayFirstCycleProcessor(replay, normal);

        var result = await scheduler.ProcessNextAsync("B", "W", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

        Assert.Equal(BranchCloudOutboxCycleDisposition.Processed, result.Disposition);
        Assert.Equal(normalCalls + 1, calls.Count);
        Assert.Equal("replay", calls[0]);
        if (normalCalls == 1) { Assert.Equal("normal", calls[1]); }
        Assert.Equal("B", replay.Branch);
        Assert.Equal("W", replay.Owner);
    }

    [Fact]
    public async Task No_work_in_both_processors_remains_idle()
    {
        var calls = new List<string>();
        var processor = new Processor("idle", calls, BranchCloudOutboxCycleDisposition.NoWork);
        var result = await new BranchCloudReplayFirstCycleProcessor(processor, processor)
            .ProcessNextAsync("B", "W", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        Assert.Equal(BranchCloudOutboxCycleDisposition.NoWork, result.Disposition);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task Replay_error_does_not_fall_through_to_normal()
    {
        var calls = new List<string>();
        var replay = new Processor("replay", calls, BranchCloudOutboxCycleDisposition.NoWork) { Fail = true };
        var normal = new Processor("normal", calls, BranchCloudOutboxCycleDisposition.Processed);
        await Assert.ThrowsAsync<IOException>(() => new BranchCloudReplayFirstCycleProcessor(replay, normal)
            .ProcessNextAsync("B", "W", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { "replay" }, calls);
    }

    [Fact]
    public async Task Cancellation_prevents_processing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = new List<string>();
        var processor = new Processor("unused", calls, BranchCloudOutboxCycleDisposition.NoWork);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BranchCloudReplayFirstCycleProcessor(processor, processor)
            .ProcessNextAsync("B", "W", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), cancellation.Token));
        Assert.Empty(calls);
    }

    private sealed class Processor(string label, List<string> calls, BranchCloudOutboxCycleDisposition result) :
        IBranchCloudReplayCycleProcessor, IBranchCloudOutboxCycleProcessor
    {
        public bool Fail { get; init; }
        public string? Branch { get; private set; }
        public string? Owner { get; private set; }
        public Task<BranchCloudOutboxCycleResult> ProcessNextAsync(string branchId, string leaseOwner,
            TimeSpan leaseDuration, TimeSpan retryDelay, CancellationToken cancellationToken = default)
        {
            calls.Add(label);
            Branch = branchId;
            Owner = leaseOwner;
            if (Fail) { throw new IOException("simulated replay error"); }
            return Task.FromResult(new BranchCloudOutboxCycleResult(result));
        }
    }
}
