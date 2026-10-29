using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Jobs;

public sealed class JobRunnerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-jobs", Guid.NewGuid().ToString("N"));
    private readonly LabPaths _paths;
    private readonly FakeTimeProvider _time = new(Start);
    private readonly LeaseBook _leases;

    public JobRunnerTests()
    {
        _paths = LabPaths.FromRoot(_root);
        _leases = new LeaseBook(_time, new NoEvents(), new LeaseOptions());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Job_SecondStart_IsRefusedWhileFirstRuns()
    {
        using var runner = CreateRunner();
        var gate = new TaskCompletionSource();
        Result<JobHandle> first = runner.Start("flake_check", 7, (ctx, ct) => WaitThenPass(gate.Task, ct));
        Assert.True(first.IsSuccess);

        Result<JobHandle> second = runner.Start("hunt", 8, (ctx, ct) => WaitThenPass(gate.Task, ct));

        Assert.True(second.IsFailure);
        Assert.StartsWith("[Lab.JobRunning] " + first.Value!.JobId, second.Error, StringComparison.Ordinal);
        gate.SetResult();
        await first.Value.Completed;
    }

    [Fact]
    public async Task Job_LeaseEnd_Cancels()
    {
        using var runner = CreateRunner();
        var relay = new LeaseEndRelay(_leases, [runner], NullLogger<LeaseEndRelay>.Instance);
        await relay.StartAsync(CancellationToken.None);
        var never = new TaskCompletionSource();
        JobHandle handle = Started(runner.Start("hunt", 9, (ctx, ct) => WaitThenPass(never.Task, ct)));

        // A takeover ends the job's lease; the relay runs the job's hook, which cancels the job.
        Assert.True(_leases.Acquire("operator", "manual", null, true, "takeover in test").IsSuccess);

        await handle.Completed;
        Assert.Equal("cancelled", runner.Get(handle.JobId).Value!.State);
        await relay.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Job_Success_WritesJobAndRunBundle_AndReleasesLease()
    {
        using var runner = CreateRunner();
        JobHandle handle = Started(runner.Start("flake_check", 11, (ctx, ct) =>
            Task.FromResult(Result<JobOutcome>.Success(new JobOutcome("pass", new JsonObject { ["flakes"] = 0 })))));

        await handle.Completed;

        string folder = Path.Combine(_paths.Jobs, handle.JobId);
        string jobText = await File.ReadAllTextAsync(Path.Combine(folder, "job.json"));
        JsonNode? job = JsonNode.Parse(jobText);
        JsonNode? run = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "run.json")));
        Assert.Equal("succeeded", job!["state"]!.GetValue<string>());
        Assert.Equal(0, job["result"]!["flakes"]!.GetValue<int>());
        Assert.Equal(handle.JobId, run!["run_id"]!.GetValue<string>());
        Assert.Equal("flake_check", run["kind"]!.GetValue<string>());
        Assert.Equal("pass", run["overall"]!.GetValue<string>());
        Assert.Null(_leases.Current);
        Assert.DoesNotContain("lease", jobText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Job_LeaseHeldByAnother_IsRefused()
    {
        using var runner = CreateRunner();
        Assert.True(_leases.Acquire("operator", "uat", null, false, null).IsSuccess);

        Result<JobHandle> started = runner.Start("hunt", 1, (ctx, ct) => Task.FromResult(Result<JobOutcome>.Success(new JobOutcome("pass", null))));

        Assert.True(started.IsFailure);
        Assert.StartsWith("[Lab.LeaseHeld]", started.Error, StringComparison.Ordinal);
        Assert.Empty(runner.List());
    }

    [Fact]
    public async Task Job_ThrownException_BecomesFailedWithoutThrowing()
    {
        using var runner = CreateRunner();
        JobHandle handle = Started(runner.Start("hunt", 2, (ctx, ct) => throw new InvalidOperationException("boom")));

        await handle.Completed;

        JobRecord record = runner.Get(handle.JobId).Value!;
        Assert.Equal("failed", record.State);
        Assert.Equal("[Lab.JobFailed]", record.Result!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Job_UnrequestedCancellation_EndsFailed_AndTheNextStartIsAccepted()
    {
        using var runner = CreateRunner();
        JobHandle handle = Started(runner.Start("hunt", 4, (ctx, ct) => throw new OperationCanceledException()));

        await handle.Completed;

        Assert.Equal("failed", runner.Get(handle.JobId).Value!.State);

        JobHandle next = Started(runner.Start("hunt", 5, (ctx, ct) => Task.FromResult(Result<JobOutcome>.Success(new JobOutcome("pass", null)))));
        await next.Completed;
        Assert.Equal("succeeded", runner.Get(next.JobId).Value!.State);
    }

    [Fact]
    public async Task Job_Cancel_EndsAsCancelled()
    {
        using var runner = CreateRunner();
        var never = new TaskCompletionSource();
        JobHandle handle = Started(runner.Start("hunt", 3, (ctx, ct) => WaitThenPass(never.Task, ct)));

        Assert.True(runner.Cancel(handle.JobId).IsSuccess);
        await handle.Completed;

        Assert.Equal("cancelled", runner.Get(handle.JobId).Value!.State);
    }

    private JobRunner CreateRunner() => new(_paths, _leases, new PathValidator(NullLogger<PathValidator>.Instance), _time,
        NullLogger<JobRunner>.Instance);

    private static JobHandle Started(Result<JobHandle> result)
    {
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private static async Task<Result<JobOutcome>> WaitThenPass(Task gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return Result<JobOutcome>.Success(new JobOutcome("pass", null));
    }

    private sealed class NoEvents : ILabEvents
    {
        public void Emit(LabEvent labEvent)
        {
        }
    }
}
