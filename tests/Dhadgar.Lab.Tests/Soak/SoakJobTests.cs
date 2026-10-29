using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Soak;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Soak;

/// <summary>
/// The soak job over fakes: samples go to the store that I7 reads, the store is ended when the job finishes or is cancelled,
/// a sampler failure is recorded and fails the outcome, and only labelled containers are sampled.
/// </summary>
public sealed class SoakJobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-soak-job", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly LabPaths _paths;
    private readonly JobRunner _runner;
    private readonly ResourceSampleStore _store = new();
    private int _uatCalls;

    public SoakJobTests()
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
    public async Task SoakJob_Samples_GoToTheStore_AndTheSoakEnds()
    {
        var docker = new FakeDocker(exitCode: 0);
        var sampler = Sampler(docker);
        Result<JobOutcome> outcome = default;

        var run = new SoakRun("lab-selftest.toml", null, 120, 9);
        Result<JobHandle> started = _runner.Start(SoakJob.Kind, run.Seed, async (context, token) =>
        {
            Result<JobOutcome> result = await SoakJob.RunAsync(run, context, Invoke, new GatewayPacer(_time), sampler, _store, _time, token);
            outcome = result;
            return result;
        });
        Assert.True(started.IsSuccess, started.Error);
        await started.Value!.Completed;

        Assert.True(_store.Snapshot().Count >= 3);
        Assert.False(_store.IsSoaking);
        Assert.Equal("pass", outcome.Value.Overall);
        Assert.True(_uatCalls > 0);
        Assert.Equal(new[] { "stats", "--no-stream", "--format", "{{json .}}", "cid-a" }, docker.Args.Take(5).ToArray());
    }

    [Fact]
    public async Task SoakJob_Cancelled_EndsTheSoak()
    {
        var docker = new FakeDocker(exitCode: 0);
        var sampler = Sampler(docker);
        var jobId = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = new SoakRun("lab-selftest.toml", ["enroll-gateway"], 600, 9);
        async Task<LabToolResult> CancelOnFirstLoop(string name, JsonElement args, LabCallContext callContext, CancellationToken ct)
        {
            _runner.Cancel(await jobId.Task);
            return await Invoke(name, args, callContext, ct);
        }

        Result<JobHandle> started = _runner.Start(SoakJob.Kind, run.Seed, (context, token) =>
            SoakJob.RunAsync(run, context, CancelOnFirstLoop, new GatewayPacer(_time), sampler, _store, _time, token));
        Assert.True(started.IsSuccess, started.Error);
        jobId.SetResult(started.Value!.JobId);

        try
        {
            await started.Value.Completed;
        }
        catch (OperationCanceledException)
        {
            // The job was cancelled; the store must still be ended.
        }

        Assert.False(_store.IsSoaking);
        Assert.NotEmpty(_store.Snapshot());
    }

    [Fact]
    public async Task SoakJob_SamplerFailure_IsRecordedAndFailsTheOutcome()
    {
        var docker = new FakeDocker(exitCode: 1);
        var sampler = Sampler(docker);
        Result<JobOutcome> outcome = default;

        var run = new SoakRun("lab-selftest.toml", null, 300, 9);
        Result<JobHandle> started = _runner.Start(SoakJob.Kind, run.Seed, async (context, token) =>
        {
            Result<JobOutcome> result = await SoakJob.RunAsync(run, context, Invoke, new GatewayPacer(_time), sampler, _store, _time, token);
            outcome = result;
            return result;
        });
        Assert.True(started.IsSuccess, started.Error);
        await started.Value!.Completed;

        Assert.NotNull(_store.LastError);
        Assert.Equal("fail", outcome.Value.Overall);
        Assert.Empty(_store.Snapshot());
    }

    private ResourceSampler Sampler(FakeDocker docker)
    {
        var registry = new FakeRegistry();
        return new ResourceSampler(
            docker,
            registry,
            ct => Task.FromResult(Result<IReadOnlyList<string>>.Success(["ml-a"])),
            _time);
    }

    private Task<LabToolResult> Invoke(string name, JsonElement args, LabCallContext callContext, CancellationToken ct)
    {
        if (name != "uat_run")
        {
            return Task.FromResult(new LabToolResult(true, JsonSerializer.SerializeToElement(new { error = "unknown " + name })));
        }

        _uatCalls++;

        // uat_run takes ten seconds of fake time, so the timeline advances and the pacer window never fills.
        _time.Advance(TimeSpan.FromSeconds(10));
        return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { overall = "pass" })));
    }

    private sealed class NoEvents : ILabEvents
    {
        public void Emit(LabEvent labEvent)
        {
        }
    }

    private sealed class FakeDocker(int exitCode) : IDockerCli
    {
        private const string Line = """{"Name":"ml-a","MemUsage":"100MiB / 1GiB","PIDs":"10"}""";

        public IReadOnlyList<string> Args { get; private set; } = [];

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Args = args;
            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(exitCode, Line, string.Empty)));
        }
    }

    private sealed class FakeRegistry : ILabTargetRegistry
    {
        private static readonly DateTimeOffset Epoch = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

        public IReadOnlyList<LabTarget> All { get; } =
        [
            new LabTarget("c:ml-a", LabTargetKind.Container, "ml-a", "cid-a", Epoch),
        ];

        public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId) =>
            throw new NotSupportedException();

        public LabTarget? Find(string targetId) => All.FirstOrDefault(target => target.TargetId == targetId);

        public void Remove(string targetId) => throw new NotSupportedException();

        public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
