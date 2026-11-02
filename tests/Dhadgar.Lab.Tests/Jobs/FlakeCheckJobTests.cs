using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Tests.Results;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Jobs;

/// <summary>
/// The flake check (<c>results_flake_check</c>): the rows it selects, the reruns it asks <c>uat_run</c> for, and the flake
/// it records when a rerun grades a row differently with the same seed and build.
/// </summary>
public sealed class FlakeCheckJobTests : IDisposable
{
    /// <summary>A seed above <c>long.MaxValue</c>: about half of all random seeds are this large (C17, F119).</summary>
    private const ulong HighSeed = 12499590173714643239UL;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-flake-check", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly LabPaths _paths;
    private readonly JobRunner _runner;

    public FlakeCheckJobTests()
    {
        _paths = LabPaths.FromRoot(_root);
        Directory.CreateDirectory(_paths.UatRuns);
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
            // A WAL file still open on a failing test; the temp folder is cleaned by the OS.
        }
    }

    [Fact]
    public async Task Plan_DefaultsToTheFailedRows()
    {
        WriteSource("src-1", new RowSpec("nodes", "enroll", "PASS"), new RowSpec("nodes", "heal", "FAIL"));

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-1", null,
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Equal("nodes.toml", plan.Value!.SpecFile);
        Assert.Equal(7UL, plan.Value.Seed);
        Assert.Equal(new[] { "heal" }, plan.Value.Rows);
        Assert.Equal(new[] { "nodes/heal" }, plan.Value.Keys);
        Assert.Equal(new[] { "nodes" }, plan.Value.Sections);
        Assert.Equal(2, plan.Value.Reruns);
    }

    [Fact]
    public async Task Plan_NamedRowNotInTheRun_IsNotFound()
    {
        WriteSource("src-2", new RowSpec("nodes", "enroll", "FAIL"));

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-2", new[] { "missing" },
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", plan.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Plan_RerunsOutsideOneToFive_IsRefused(int reruns)
    {
        WriteSource("src-3", new RowSpec("nodes", "enroll", "FAIL"));

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-3", null, reruns,
            CancellationToken.None);

        Assert.True(plan.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plan_RunWithNoFailedRows_IsRefused()
    {
        WriteSource("src-4", new RowSpec("nodes", "enroll", "PASS"));

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-4", null,
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_SeedAboveLongMax_PlansWithSameSeed()
    {
        string folder = WriteSource("src-high", new RowSpec("nodes", "enroll", "FAIL"));
        SetSeed(folder, HighSeed);

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-high", null,
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Equal(HighSeed, plan.Value!.Seed);
    }

    [Fact]
    public async Task RunAsync_SeedAboveLongMax_ResultSeedIsUnsigned()
    {
        string folder = WriteSource("src-high-run", new RowSpec("nodes", "enroll", "FAIL"));
        SetSeed(folder, HighSeed);
        var calls = new List<JsonElement>();

        string jobId = await StartJobAsync("src-high-run", (name, args, callContext, ct) =>
        {
            calls.Add(args.Clone());
            string rerunId = "rerun-high-" + calls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            BundleFixtures.Write(_paths.UatRuns, rerunId, "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
            return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { run_id = rerunId, overall = "pass" })));
        });

        JobRecord record = _runner.Get(jobId).Value!;
        Assert.Equal(HighSeed, record.Result!["seed"]!.GetValue<ulong>());
        Assert.Equal(HighSeed, calls[0].GetProperty("seed").GetUInt64());
    }

    [Fact]
    public async Task PlanAsync_RowsAsKey_SelectsThatRow()
    {
        WriteSource("src-key", new RowSpec("nodes", "enroll", "PASS"), new RowSpec("nodes", "heal", "FAIL"));

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-key", new[] { "nodes/heal" },
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Equal(new[] { "heal" }, plan.Value!.Rows);
        Assert.Equal(new[] { "nodes/heal" }, plan.Value.Keys);
    }

    [Fact]
    public async Task PlanAsync_RowArgTooLong_IsInvalidWithoutEcho()
    {
        WriteSource("src-long-row", new RowSpec("nodes", "heal", "FAIL"));
        string longRow = new string('a', 201);

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-long-row", new[] { longRow },
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", plan.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(longRow, plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_RowArgWithNewline_IsInvalidWithoutEcho()
    {
        WriteSource("src-newline-row", new RowSpec("nodes", "heal", "FAIL"));
        const string badRow = "heal\nx";

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-newline-row", new[] { badRow },
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", plan.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(badRow, plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_BareRowIdInTwoSections_IsAmbiguous()
    {
        WriteSource("src-ambiguous", new RowSpec("a", "x", "FAIL"), new RowSpec("b", "x", "FAIL"));

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-ambiguous", new[] { "x" },
            FlakeCheckJob.DefaultReruns, CancellationToken.None);

        Assert.True(plan.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", plan.Error, StringComparison.Ordinal);
        Assert.Contains("ambiguous", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_RerunGradesRowDifferently_IsFlakyFailsAndIngestsOneFlake()
    {
        using ResultsDb db = ResultsDb.Open(Path.Combine(_root, "results"), _validator).Value;
        var ingestor = new BundleIngestor(db, _validator, KnownIssueCheck.Of(KnownIssues.Empty, "test"), text => text, [],
            NullLogger<BundleIngestor>.Instance);
        string source = WriteSource("src-5", new RowSpec("nodes", "enroll", "FAIL"));
        Assert.True((await ingestor.IngestAsync(source, [_paths.UatRuns], CancellationToken.None)).IsSuccess);

        var calls = new List<JsonElement>();
        async Task<LabToolResult> Invoke(string name, JsonElement args, LabCallContext callContext, CancellationToken ct)
        {
            calls.Add(args.Clone());
            string rerunId = "rerun-" + calls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string folder = BundleFixtures.Write(_paths.UatRuns, rerunId, "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
            Assert.True((await ingestor.IngestAsync(folder, [_paths.UatRuns], ct)).IsSuccess);
            return new LabToolResult(false, JsonSerializer.SerializeToElement(new { run_id = rerunId, overall = "pass" }));
        }

        string jobId = await StartJobAsync("src-5", Invoke);

        JobRecord record = _runner.Get(jobId).Value!;
        Assert.Equal("succeeded", record.State);
        Assert.Equal("flaky", record.Result!["state"]!.GetValue<string>());
        Assert.Single(record.Result["flakes"]!.AsArray());
        Assert.Equal(2, calls.Count);
        Assert.Equal("nodes.toml", calls[0].GetProperty("spec").GetString());
        Assert.Equal(7UL, calls[0].GetProperty("seed").GetUInt64());
        Assert.Equal(1, db.CountFlakes());

        // The job's lease id goes to uat_run and to nothing the job writes or returns.
        string leaseId = calls[0].GetProperty("lease_id").GetString()!;
        Assert.False(string.IsNullOrEmpty(leaseId));
        Assert.DoesNotContain(leaseId, record.Result.ToJsonString(), StringComparison.Ordinal);
        string jobFile = await File.ReadAllTextAsync(Path.Combine(_paths.Jobs, jobId, "job.json"));
        Assert.DoesNotContain(leaseId, jobFile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_RerunsAgreeOnPass_IsPassAndStable()
    {
        WriteSource("src-6", new RowSpec("nodes", "enroll", "PASS"));

        string jobId = await StartJobAsync("src-6", (name, args, callContext, ct) =>
        {
            string rerunId = "rerun-p-" + Guid.NewGuid().ToString("N")[..8];
            BundleFixtures.Write(_paths.UatRuns, rerunId, "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
            return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { run_id = rerunId, overall = "pass" })));
        }, new[] { "enroll" });

        JobRecord record = _runner.Get(jobId).Value!;
        Assert.Equal("stable", record.Result!["state"]!.GetValue<string>());
        Assert.Empty(record.Result["flakes"]!.AsArray());
        Assert.Equal("pass", ReadJobOverall(jobId));
    }

    [Fact]
    public async Task FlakeCheck_UnselectedFailingRow_IsNotAFlake()
    {
        // The source run has a PASS row and a FAIL row; only the PASS row is named. The FAIL row is never rerun, so it must not
        // read as a flake ("missing" in the reruns).
        WriteSource("src-unselected", new RowSpec("nodes", "enroll", "PASS"), new RowSpec("nodes", "heal", "FAIL"));

        string jobId = await StartJobAsync("src-unselected", (name, args, callContext, ct) =>
        {
            string rerunId = "rerun-u-" + Guid.NewGuid().ToString("N")[..8];
            BundleFixtures.Write(_paths.UatRuns, rerunId, "b1", 7, new RowSpec("nodes", "enroll", "PASS"));
            return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { run_id = rerunId, overall = "pass" })));
        }, new[] { "enroll" });

        JobRecord record = _runner.Get(jobId).Value!;
        Assert.Equal("stable", record.Result!["state"]!.GetValue<string>());
        Assert.Empty(record.Result["flakes"]!.AsArray());
    }

    [Fact]
    public async Task Job_ConsistentFailure_IsStableAndFails()
    {
        WriteSource("src-7", new RowSpec("nodes", "enroll", "FAIL"));

        string jobId = await StartJobAsync("src-7", (name, args, callContext, ct) =>
        {
            string rerunId = "rerun-f-" + Guid.NewGuid().ToString("N")[..8];
            BundleFixtures.Write(_paths.UatRuns, rerunId, "b1", 7, new RowSpec("nodes", "enroll", "FAIL", ClauseState: "NotHeld"));
            return Task.FromResult(new LabToolResult(false, JsonSerializer.SerializeToElement(new { run_id = rerunId, overall = "fail" })));
        });

        JobRecord record = _runner.Get(jobId).Value!;
        Assert.Equal("stable", record.Result!["state"]!.GetValue<string>());
        Assert.Empty(record.Result["flakes"]!.AsArray());
        Assert.Equal("fail", ReadJobOverall(jobId));
    }

    [Fact]
    public async Task WithSpec_FailedRowWithDependency_PassesUatRunFilterCheck()
    {
        // The source run failed row b, which depends on a. A flake check that reruns only b is refused by uat_run's check,
        // so the plan must carry the closed filter a, b while Keys stays b.
        WriteSource("src-dep", new RowSpec("chain", "a", "PASS"), new RowSpec("chain", "b", "FAIL"));
        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-dep", null,
            FlakeCheckJob.DefaultReruns, CancellationToken.None);
        Assert.True(plan.IsSuccess, plan.Error);
        UatSpec spec = LoadSpec(ChainSpec);

        Result<FlakeCheckPlan> closed = FlakeCheckJob.WithSpec(plan.Value!, spec);

        Assert.True(closed.IsSuccess, closed.Error);
        Assert.True(RowFilterCheck.Check(spec, closed.Value!.Rows).IsSuccess);
        Assert.Equal(new[] { "a", "b" }, closed.Value.Rows);
        Assert.Equal(new[] { "chain/b" }, closed.Value.Keys);
    }

    [Fact]
    public async Task WithSpec_UnknownRow_IsNotFound()
    {
        WriteSource("src-unknown", new RowSpec("chain", "b", "FAIL"));
        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, "src-unknown", null,
            FlakeCheckJob.DefaultReruns, CancellationToken.None);
        Assert.True(plan.IsSuccess, plan.Error);
        UatSpec spec = LoadSpec(SingleRowSpec);

        Result<FlakeCheckPlan> closed = FlakeCheckJob.WithSpec(plan.Value!, spec);

        Assert.True(closed.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", closed.Error, StringComparison.Ordinal);
    }

    private const string ChainSpec = """
        schema = 1

        [section]
        id = "chain"
        title = "Chain"
        sequential = true
        requires = []
        allow_invariants = []

        [[row]]
        id = "a"
        title = "A"
        expected = "a holds"

        [[row]]
        id = "b"
        title = "B"
        expected = "b holds"
        depends_on = ["a"]
        """;

    private const string SingleRowSpec = """
        schema = 1

        [section]
        id = "chain"
        title = "Chain"
        sequential = true
        requires = []
        allow_invariants = []

        [[row]]
        id = "a"
        title = "A"
        expected = "a holds"
        """;

    private static UatSpec LoadSpec(string text)
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("chain.toml", text);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }

    /// <summary>Rewrites a bundle's run.json seed as an unsigned number (the fixture writes a long).</summary>
    private static void SetSeed(string folder, ulong seed)
    {
        string file = Path.Combine(folder, "run.json");
        var run = (JsonNode.Parse(File.ReadAllText(file)) as JsonObject)!;
        run["seed"] = seed;
        File.WriteAllText(file, run.ToJsonString());
    }

    /// <summary>Writes a source bundle with one spec file (<c>nodes.toml</c>) in its run.json, the shape a real run has.</summary>
    private string WriteSource(string runId, params RowSpec[] rows)
    {
        string folder = BundleFixtures.Write(_paths.UatRuns, runId, "b1", 7, rows);
        string file = Path.Combine(folder, "run.json");
        var run = (JsonNode.Parse(File.ReadAllText(file)) as JsonObject)!;
        run["specs"] = new JsonArray(new JsonObject { ["file"] = "nodes.toml", ["sha256"] = "00" });
        File.WriteAllText(file, run.ToJsonString());
        return folder;
    }

    /// <summary>Starts the flake check job over the given source run and waits for it to end.</summary>
    private async Task<string> StartJobAsync(string sourceRunId,
        Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke, string[]? named = null)
    {
        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, sourceRunId, named,
            FlakeCheckJob.DefaultReruns, CancellationToken.None);
        Assert.True(plan.IsSuccess, plan.Error);

        Result<JobHandle> started = _runner.Start(FlakeCheckJob.Kind, plan.Value!.Seed,
            (context, ct) => FlakeCheckJob.RunAsync(plan.Value, context, invoke, _paths.UatRuns, _validator, ct));
        Assert.True(started.IsSuccess, started.Error);
        await started.Value!.Completed;
        return started.Value.JobId;
    }

    private string ReadJobOverall(string jobId)
    {
        var run = (JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.Jobs, jobId, "run.json"))) as JsonObject)!;
        return run["overall"]!.GetValue<string>();
    }

    private sealed class NoEvents : ILabEvents
    {
        public void Emit(LabEvent labEvent)
        {
        }
    }
}
