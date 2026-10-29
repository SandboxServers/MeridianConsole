using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Chaos;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Chaos;

/// <summary>
/// The chaos job with fake tools: every fault it starts is healed, also when the job is cancelled (A2), the checkpoints
/// count violations, and the timeline is written to <c>repro.toml</c>.
/// </summary>
public sealed class ChaosJobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-chaos-job", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly LabPaths _paths;
    private readonly JobRunner _runner;
    private readonly List<(string Name, JsonElement Args, CancellationToken Token)> _calls = [];
    private int _faultCount;
    private int _violationsPerCheck;

    public ChaosJobTests()
    {
        _paths = LabPaths.FromRoot(_root);
        Directory.CreateDirectory(_paths.Jobs);
        _runner = new JobRunner(_paths, new LeaseBook(_time, new NoEvents(), new LeaseOptions()), _validator, _time,
            NullLogger<JobRunner>.Instance);
    }

    public void Dispose()
    {
        _runner.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still open on a failing test; the temp folder is cleaned by the OS.
        }
    }

    [Fact]
    public async Task ChaosJob_Cancelled_HealsEverything()
    {
        ChaosRun run = Run(durationS: 600, quietS: 1);
        var jobId = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<LabToolResult> Invoke(string name, JsonElement args, LabCallContext callContext, CancellationToken ct)
        {
            _calls.Add((name, args.Clone(), ct));
            if (name == "fault_toxic_add")
            {
                // Cancel the job while the first fault is being injected; the fault is in place when the call returns.
                _ = _runner.Cancel(await jobId.Task.ConfigureAwait(false));
            }

            return await Respond(name).ConfigureAwait(false);
        }

        JobHandle handle = StartJob(run, Invoke);
        jobId.SetResult(handle.JobId);
        await handle.Completed;

        JobRecord record = _runner.Get(handle.JobId).Value!;
        Assert.Equal("cancelled", record.State);

        Assert.Equal(1, _calls.Count(call => call.Name == "fault_toxic_add"));
        var healed = _calls.Where(call => call.Name == "fault_heal").ToList();
        Assert.Single(healed);
        Assert.Equal("f1", healed[0].Args.GetProperty("fault_id").GetString());
        Assert.False(healed[0].Token.IsCancellationRequested, "the heal must run on its own live budget token");
    }

    [Fact]
    public async Task ChaosJob_RunToEnd_HealsWhatItStartedAndPasses()
    {
        ChaosRun run = Run(durationS: 30, quietS: 10);

        JobHandle handle = StartJob(run, Invoke);
        await handle.Completed;

        JobRecord record = _runner.Get(handle.JobId).Value!;
        Assert.Equal("succeeded", record.State);
        Assert.Equal(2, record.Result!["faults_started"]!.GetValue<int>());
        Assert.Equal(2, record.Result!["faults_healed"]!.GetValue<int>());
        Assert.Equal(0, record.Result!["violations"]!.GetValue<int>());
        Assert.Equal(2, _calls.Count(call => call.Name == "fault_heal"));
        Assert.Equal(0, _calls.Count(call => call.Name == "fault_heal" && call.Token.IsCancellationRequested));
    }

    [Fact]
    public async Task ChaosJob_InvariantViolation_IsCountedAtTheCheckpoint()
    {
        ChaosRun run = Run(durationS: 60, quietS: 10);
        _violationsPerCheck = 1;

        JobHandle handle = StartJob(run, Invoke);
        await handle.Completed;

        JobRecord record = _runner.Get(handle.JobId).Value!;
        Assert.True(record.Result!["checkpoints"]!.GetValue<int>() >= 1, "a checkpoint runs every 30 seconds");
        Assert.True(record.Result!["violations"]!.GetValue<int>() > 0, "a checkpoint with a violation must be counted");
    }

    [Fact]
    public async Task ChaosJob_WritesTheTimelineToReproToml()
    {
        ChaosRun run = Run(durationS: 30, quietS: 10);

        JobHandle handle = StartJob(run, Invoke);
        await handle.Completed;

        string repro = Path.Combine(_paths.Jobs, handle.JobId, ChaosJob.ReproFile);
        string text = await File.ReadAllTextAsync(repro);
        Assert.Contains("schema = 1", text, StringComparison.Ordinal);
        Assert.Contains("plan = \"fault-test\"", text, StringComparison.Ordinal);
        Assert.Contains("event = \"start\"", text, StringComparison.Ordinal);
        Assert.Contains("event = \"heal\"", text, StringComparison.Ordinal);
    }

    private sealed class NoEvents : ILabEvents
    {
        public void Emit(LabEvent labEvent)
        {
        }
    }

    private static ChaosRun Run(int durationS, int quietS)
    {
        var plan = new ChaosPlan(
            "fault-test",
            new ChaosWorkload("docs/guides/uat-specs/lab-selftest.toml", ["enroll-gateway"]),
            MaxConcurrent: 2,
            MinQuietS: quietS,
            MaxQuietS: quietS,
            Faults: [new ChaosFault("fault_toxic_add",
                new JsonObject { ["proxy"] = "nodes-postgres", ["toxic"] = "latency" }, 1, 300, 300)]);
        return ChaosJob.Prepare(plan, 7, durationS);
    }

    private JobHandle StartJob(ChaosRun run, Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke)
    {
        var pacer = new GatewayPacer(_time);
        Result<JobHandle> started = _runner.Start(ChaosJob.Kind, run.Seed, (context, token) =>
            ChaosJob.RunAsync(run, context, invoke, pacer, _time, _validator, token));
        Assert.True(started.IsSuccess, started.Error);
        return started.Value!;
    }

    /// <summary>
    /// The fake tools. <c>uat_run</c> takes ten seconds of fake time, so the timeline advances and the Gateway pacer's
    /// window never fills; the first fault is <c>f1</c>.
    /// </summary>
    private async Task<LabToolResult> Invoke(string name, JsonElement args, LabCallContext callContext, CancellationToken ct)
    {
        _calls.Add((name, args.Clone(), ct));
        await Task.Yield();
        return await Respond(name).ConfigureAwait(false);
    }

    private Task<LabToolResult> Respond(string name)
    {
        switch (name)
        {
            case "uat_run":
                _time.Advance(TimeSpan.FromSeconds(10));
                return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { run_id = "run-chaos", overall = "pass" })));
            case "invariants_check":
                return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new
                {
                    count = _violationsPerCheck,
                    violations = Enumerable.Repeat("I1", _violationsPerCheck),
                    errors = Array.Empty<string>(),
                })));
            case "fault_toxic_add":
                _faultCount++;
                return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { fault_id = "f" + _faultCount })));
            case "fault_heal":
                return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { healed = true })));
            default:
                return Task.FromResult(new LabToolResult(true, JsonSerializer.SerializeToElement(new { error = "unknown " + name })));
        }
    }
}
