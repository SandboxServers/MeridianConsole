using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Hunt;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Hunt;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Hunt;

/// <summary>
/// The enrollment model hunter (L27 step 3) against an in-memory lab. The fake answers the lab tools the hunter calls
/// and models the product: a token is used once, Postgres cut means no 2xx, and the DB counts follow the state.
/// </summary>
public sealed class ModelHunterTests : IDisposable
{
    private const ulong Seed = 20261028;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-hunter", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Hunter_PlantedReuseBug_ShrinksAndReplays()
    {
        var lab = new FakeLab { ReusedTokenReturns201 = true };
        ModelHunter hunter = NewHunter(lab);

        Result<JobOutcome> first = await hunter.RunAsync(Seed, 200, 30, _root, "build-1", CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal("fail", first.Value!.Overall);

        JsonObject node = first.Value.Result!.AsObject();
        List<HuntOp> ops = ParseOps(node["ops"]!.AsArray());
        Assert.InRange(ops.Count, 1, 3);
        Assert.Contains(ops, op => op.Kind == HuntOpKind.Enroll || op.Kind == HuntOpKind.EnrollAgain);

        Result<ModelHunter.CaseResult> replay = await hunter.ReplayAsync(ops, CancellationToken.None);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.True(replay.Value!.Diverged);

        Assert.True(File.Exists(Path.Combine(_root, "repro", "repro.toml")));
        string toml = await File.ReadAllTextAsync(Path.Combine(_root, "repro", "repro.toml"));
        Assert.Contains("kind = \"hunt\"", toml, StringComparison.Ordinal);
        Assert.Contains("first_diverging_op = ", toml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hunter_SameSeed_SameFirstOpList()
    {
        ModelHunter hunter = NewHunter(new FakeLab { ReusedTokenReturns201 = true });
        Result<JobOutcome> one = await hunter.RunAsync(Seed, 200, 30, null, "build-1", CancellationToken.None);
        Result<JobOutcome> two = await hunter.RunAsync(Seed, 200, 30, null, "build-1", CancellationToken.None);

        Assert.Equal(one.Value!.Result!["ops"]!.ToJsonString(), two.Value!.Result!["ops"]!.ToJsonString());
    }

    [Fact]
    public async Task Hunter_PgDownEnroll500_IsKnownNotNew()
    {
        var lab = new FakeLab { Pg500 = true };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 150, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("pass", outcome.Value!.Overall);
        Assert.True(outcome.Value.Result!["known"]!.GetValue<int>() > 0, "a cut-window 500 must be counted as known");
    }

    [Fact]
    public async Task Hunter_I1OutsidePgDown_Fails()
    {
        var lab = new FakeLab { I1Violations = true };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("fail", outcome.Value!.Overall);
        Assert.Contains("I1", outcome.Value.Result!["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hunter_Cancelled_HealsWithOwnToken()
    {
        var lab = new FakeLab();
        using var jobCancel = new CancellationTokenSource();
        lab.OnFirstPgDown = () => jobCancel.Cancel();
        ModelHunter hunter = NewHunter(lab);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hunter.RunAsync(Seed, 200, 30, null, "build-1", jobCancel.Token));

        int disable = lab.Calls.FindIndex(call => call.Tool == "fault_proxy_disable");
        Assert.True(disable >= 0, "the case must cut Postgres before the cancel");
        Assert.Contains(lab.Calls.Skip(disable + 1), call => call.Tool == "fault_reset" && !call.Cancelled);
    }

    [Fact]
    public async Task Hunter_OpenToolCalls_CarryNoLeaseId_LeasedToolCallsDo()
    {
        // wave 5 live gate: db_query and invariants_check are Open and reject lease_id (Lab.InvalidArgument).
        var lab = new FakeLab();
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);
        Assert.True(outcome.IsSuccess, outcome.Error);

        var openCalls = lab.Arguments.Where(call => call.Tool is "db_query" or "invariants_check").ToList();
        Assert.NotEmpty(openCalls);
        foreach ((string tool, JsonElement args) in openCalls)
        {
            Assert.False(args.TryGetProperty("lease_id", out _), tool + " must not carry lease_id");
        }

        var leased = lab.Arguments.Where(call => call.Tool == "nodes_token_mint").ToList();
        Assert.NotEmpty(leased);
        Assert.All(leased, call => Assert.True(call.Args.TryGetProperty("lease_id", out _), "a Leased tool keeps lease_id"));
    }

    [Fact]
    public async Task Hunter_I1OutsidePgDown_FailsTheCase()
    {
        // The fake answers the real InvariantViolation shape, so the hunter must read invariant_id (F92).
        var lab = new FakeLab { I1Violations = true };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("fail", outcome.Value!.Overall);
        Assert.Contains("I1 violated outside a PgDown window", outcome.Value.Result!["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hunter_429ThenOk_RetriesOnce()
    {
        var lab = new FakeLab { Throttles = 1 };
        Result<JobOutcome> outcome = await RunAdvancingClockAsync(NewHunter(lab));

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("pass", outcome.Value!.Overall);
        Assert.Equal(0, lab.Throttles);
        Assert.Equal(0, outcome.Value.Result!["throttled"]!.GetValue<int>());
    }

    [Fact]
    public async Task Hunter_429Twice_IsThrottled()
    {
        var lab = new FakeLab { Throttles = 2 };
        Result<JobOutcome> outcome = await RunAdvancingClockAsync(NewHunter(lab));

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal(0, lab.Throttles);
        Assert.True(outcome.Value!.Result!["throttled"]!.GetValue<int>() >= 1, "a case that is throttled twice must be counted as throttled");
    }

    [Fact]
    public async Task Hunter_MintWithoutTokenRef_IsInfrastructure()
    {
        var lab = new FakeLab { MintWithoutTokenRef = true };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsFailure, "a mint with no token_ref is an infrastructure error, not a product verdict");
        Assert.Contains("nodes_token_mint returned no token_ref", outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hunter_HealThrows_CaseStillEnds()
    {
        var lab = new FakeLab { HealThrows = true };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 150, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("pass", outcome.Value!.Overall);
        Assert.Contains(lab.Calls, call => call.Tool == "fault_proxy_disable");
    }

    [Fact]
    public async Task Replay_RestartInsideCut_IsNotSent()
    {
        // D-ML119: Nodes does not start while its database is cut (F104), so the hunt sends no svc_restart there.
        var lab = new FakeLab { RestartFailsWhileCut = true };
        ModelHunter hunter = NewHunter(lab);

        Result<ModelHunter.CaseResult> replay = await hunter.ReplayAsync([new HuntOp(HuntOpKind.PgDown), new HuntOp(HuntOpKind.RestartNodes), new HuntOp(HuntOpKind.PgUp), new HuntOp(HuntOpKind.Mint)], CancellationToken.None);

        Assert.True(replay.IsSuccess, replay.Error);
        Assert.False(replay.Value!.Diverged);
        int cutStart = lab.Calls.FindIndex(call => call.Tool == "fault_proxy_disable");
        int cutEnd = lab.Calls.FindIndex(call => call.Tool == "fault_heal");
        Assert.DoesNotContain(lab.Calls.Skip(cutStart).Take(cutEnd - cutStart), call => call.Tool == "svc_restart");
        // The case-start restart is outside any cut, so it is the only svc_restart before the case's data reset.
        int dataReset = lab.Calls.FindIndex(call => call.Tool == "stack_reset_data");
        Assert.Equal(1, lab.Calls.Take(dataReset).Count(call => call.Tool == "svc_restart"));
    }

    [Fact]
    public async Task Replay_PgDownInsideCut_IsNotSentTwice()
    {
        // D-ML122: a PgDown while the cut is active sends nothing, so FaultBook sees one disable.
        var lab = new FakeLab { DisableClashes = true };
        ModelHunter hunter = NewHunter(lab);

        Result<ModelHunter.CaseResult> replay = await hunter.ReplayAsync([new HuntOp(HuntOpKind.PgDown), new HuntOp(HuntOpKind.PgDown), new HuntOp(HuntOpKind.PgUp), new HuntOp(HuntOpKind.Mint)], CancellationToken.None);

        Assert.True(replay.IsSuccess, replay.Error);
        Assert.False(replay.Value!.Diverged);
        Assert.Equal(1, lab.Calls.Count(call => call.Tool == "fault_proxy_disable"));
        Assert.Equal(1, lab.Calls.Count(call => call.Tool == "fault_heal"));
    }

    [Fact]
    public async Task Replay_PgDownAfterHeal_IsSentAgain()
    {
        var lab = new FakeLab { DisableClashes = true };
        ModelHunter hunter = NewHunter(lab);

        Result<ModelHunter.CaseResult> replay = await hunter.ReplayAsync([new HuntOp(HuntOpKind.PgDown), new HuntOp(HuntOpKind.PgUp), new HuntOp(HuntOpKind.PgDown), new HuntOp(HuntOpKind.PgUp)], CancellationToken.None);

        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(2, lab.Calls.Count(call => call.Tool == "fault_proxy_disable"));
        Assert.Equal(2, lab.Calls.Count(call => call.Tool == "fault_heal"));
    }

    [Fact]
    public async Task Replay_RestartOutsideCut_IsSent()
    {
        var lab = new FakeLab();
        ModelHunter hunter = NewHunter(lab);

        Result<ModelHunter.CaseResult> replay = await hunter.ReplayAsync([new HuntOp(HuntOpKind.Mint), new HuntOp(HuntOpKind.RestartNodes)], CancellationToken.None);

        Assert.True(replay.IsSuccess, replay.Error);
        // The new hunter's case-start restart, then the op's restart after the data reset.
        Assert.Equal(2, lab.Calls.Count(call => call.Tool == "svc_restart"));
        int dataReset = lab.Calls.FindIndex(call => call.Tool == "stack_reset_data");
        Assert.Contains(lab.Calls.Skip(dataReset), call => call.Tool == "svc_restart");
    }

    [Fact]
    public async Task Hunter_WrittenRepro_ParsesWithReproParser()
    {
        var lab = new FakeLab { ReusedTokenReturns201 = true };
        Result<JobOutcome> first = await NewHunter(lab).RunAsync(Seed, 200, 30, _root, "build-1", CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal("fail", first.Value!.Overall);

        string toml = await File.ReadAllTextAsync(Path.Combine(_root, "repro", "repro.toml"));
        Result<HuntModelTool.ReproRecord> parsed = HuntModelTool.ReproParser.Parse(toml);
        Assert.True(parsed.IsSuccess, parsed.Error);

        List<string> written = first.Value.Result!["ops"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        Assert.Equal(written, parsed.Value!.Ops.Select(op => op.ToString()));
    }

    [Fact]
    public async Task Hunter_StalePoolAfterCut_ReportsAReproducibleCase()
    {
        // Run 9: a healed cut leaves the pool stale, so an enroll after PgUp answers 500. The hunt must report it, not shrink it away.
        var lab = new FakeLab { StalePoolAfterCut = true };
        ModelHunter hunter = NewHunter(lab);

        Result<JobOutcome> outcome = await hunter.RunAsync(Seed, 200, 30, null, "build-1", CancellationToken.None);
        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("fail", outcome.Value!.Overall);

        JsonObject node = outcome.Value.Result!.AsObject();
        Assert.Equal("diverged", node["state"]!.GetValue<string>());
        List<HuntOp> ops = ParseOps(node["ops"]!.AsArray());
        int pgDown = ops.FindIndex(op => op.Kind == HuntOpKind.PgDown);
        Assert.True(pgDown >= 0 && ops.FindIndex(pgDown, op => op.Kind is HuntOpKind.Enroll or HuntOpKind.EnrollAgain) > pgDown,
            "the repro must cut Postgres before an enroll");

        // F120: the reported case replays on a new hunter over a new lab with the same flags, as the next job would.
        Result<ModelHunter.CaseResult> replay = await NewHunter(new FakeLab { StalePoolAfterCut = true }).ReplayAsync(ops, CancellationToken.None);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.True(replay.Value!.Diverged);
    }

    [Fact]
    public async Task Hunter_CarriedI1_IsCountedNotDiverged()
    {
        // An I1 queued before the run is carried into the first case's checkpoint: counted, and the case still passes.
        var lab = new FakeLab { StaleI1AtStart = 1 };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("pass", outcome.Value!.Overall);
        Assert.Equal(1, outcome.Value.Result!["carried"]!.GetValue<int>());
    }

    [Fact]
    public async Task Hunter_CaseStart_RestartsWhenNewCutOrAfterCut()
    {
        // F120: a case restarts Nodes at its start when the hunter is new, the case cuts Postgres, or the previous case cut.
        var lab = new FakeLab();
        ModelHunter hunter = NewHunter(lab);
        var cases = new (HuntOp[] Ops, bool Restarts)[]
        {
            ([new HuntOp(HuntOpKind.Mint)], true),
            ([new HuntOp(HuntOpKind.Mint)], false),
            ([new HuntOp(HuntOpKind.PgDown), new HuntOp(HuntOpKind.PgUp)], true),
            ([new HuntOp(HuntOpKind.Mint)], true),
        };

        foreach ((HuntOp[] ops, bool restarts) in cases)
        {
            int before = lab.Calls.Count;
            Result<ModelHunter.CaseResult> replay = await hunter.ReplayAsync(ops, CancellationToken.None);
            Assert.True(replay.IsSuccess, replay.Error);

            int dataReset = lab.Calls.FindIndex(before, call => call.Tool == "stack_reset_data");
            bool restarted = lab.Calls.Skip(before).Take(dataReset - before).Any(call => call.Tool == "svc_restart");
            Assert.Equal(restarts, restarted);
        }
    }

    [Fact]
    public async Task Replay_NewHunter_SameResultAsWarmHunter()
    {
        // F120, run 10: the same cut case diverges on a warm hunter and on a new one, because a new hunter restarts and warms first.
        var labA = new FakeLab { StalePoolAfterCut = true };
        ModelHunter hunterA = NewHunter(labA);
        var clean = new[] { new HuntOp(HuntOpKind.Mint), new HuntOp(HuntOpKind.Enroll, 0) };
        var cut = new[] { new HuntOp(HuntOpKind.Mint), new HuntOp(HuntOpKind.PgDown), new HuntOp(HuntOpKind.PgUp), new HuntOp(HuntOpKind.Enroll, 0) };

        Assert.True((await hunterA.ReplayAsync(clean, CancellationToken.None)).IsSuccess);
        Result<ModelHunter.CaseResult> warm = await hunterA.ReplayAsync(cut, CancellationToken.None);
        Assert.True(warm.IsSuccess, warm.Error);
        Assert.True(warm.Value!.Diverged);
        Assert.Equal(3, warm.Value.Index);

        var labB = new FakeLab { StalePoolAfterCut = true };
        Result<ModelHunter.CaseResult> fresh = await NewHunter(labB).ReplayAsync(cut, CancellationToken.None);
        Assert.True(fresh.IsSuccess, fresh.Error);
        Assert.True(fresh.Value!.Diverged);
        Assert.Equal(3, fresh.Value.Index);
    }

    [Fact]
    public async Task Hunter_NewHunter_FirstCaseRestartsAndWarms()
    {
        var lab = new FakeLab();
        Result<ModelHunter.CaseResult> replay = await NewHunter(lab).ReplayAsync([new HuntOp(HuntOpKind.Mint)], CancellationToken.None);
        Assert.True(replay.IsSuccess, replay.Error);

        int dataReset = lab.Calls.FindIndex(call => call.Tool == "stack_reset_data");
        string[] before = lab.Calls.Take(dataReset).Select(call => call.Tool).ToArray();
        Assert.Equal(new[] { "fault_reset", "svc_restart", "nodes_token_mint", "nodes_enroll_probe" }, before);

        JsonElement probe = lab.Arguments.Single(call => call.Tool == "nodes_enroll_probe").Args;
        Assert.Equal("hunt-warmup", probe.GetProperty("hostname").GetString());
        Assert.Equal("nodes", probe.GetProperty("via").GetString());
    }

    [Fact]
    public async Task Hunter_WarmUpNot201_IsInfrastructure()
    {
        var lab = new FakeLab { WarmUpStatus = 503 };
        Result<ModelHunter.CaseResult> replay = await NewHunter(lab).ReplayAsync([new HuntOp(HuntOpKind.Mint)], CancellationToken.None);

        Assert.False(replay.IsSuccess);
        Assert.Equal("[Lab.Unavailable] nodes warm-up enroll answered 503", replay.Error);
    }

    [Fact]
    public async Task Hunter_RunSeedAboveLongMax_PayloadUnsigned()
    {
        const ulong seed = 12499590173714643239UL;
        Result<JobOutcome> outcome = await NewHunter(new FakeLab { StalePoolAfterCut = true }).RunAsync(seed, 200, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("fail", outcome.Value!.Overall);
        Assert.Equal(seed, outcome.Value.Result!["run_seed"]!.GetValue<ulong>());
    }

    [Fact]
    public async Task Hunter_ShrinkNotReproduced_ReportsOriginal()
    {
        // One I1 on the second invariants_check only: the original fails, and no shrink candidate sees that check again.
        var lab = new FakeLab { FlakyI1OnCheck = 2 };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("fail", outcome.Value!.Overall);
        JsonObject node = outcome.Value.Result!.AsObject();
        Assert.NotEmpty(node["original_ops"]!.AsArray());
        Assert.NotNull(node["original_detail"]?.GetValue<string>());
    }

    [Fact]
    public async Task Hunter_ShrinkNotReproduced_OriginalReproduces_ReportsDivergedAndShrinkUnstable()
    {
        // The original case fails on its third checked op and arms one flake for the next case. The shrink lands on that next case,
        // which replays clean; the original then replays dirty and is reported with shrink_unstable.
        var lab = new FakeLab { I1FromThirdOp = true };
        Result<JobOutcome> outcome = await NewHunter(lab).RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal("fail", outcome.Value!.Overall);
        JsonObject node = outcome.Value.Result!.AsObject();
        Assert.Equal("diverged", node["state"]!.GetValue<string>());
        Assert.True(node["shrink_unstable"]!.GetValue<bool>());
        Assert.Equal(node["original_ops"]!.ToJsonString(), node["ops"]!.ToJsonString());
        Assert.True(ParseOps(node["ops"]!.AsArray()).Count > 1, "the reported ops are the original, not the one-op shrink");
    }

    /// <summary>Runs the hunt while advancing the fake clock, so the hunter's wait after a 429 ends.</summary>
    private async Task<Result<JobOutcome>> RunAdvancingClockAsync(ModelHunter hunter)
    {
        Task<Result<JobOutcome>> run = hunter.RunAsync(Seed, 50, 30, null, "build-1", CancellationToken.None);
        for (int i = 0; i < 5000 && !run.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
        }

        return await run.ConfigureAwait(false);
    }

    private ModelHunter NewHunter(FakeLab lab) =>
        new(lab.InvokeAsync, new GatewayPacer(_time), _time, _validator, "lease-hunt-test", "nodes", TimeSpan.Zero);

    private static List<HuntOp> ParseOps(JsonArray array)
    {
        var ops = new List<HuntOp>();
        foreach (JsonNode? item in array)
        {
            string text = item!.GetValue<string>();
            int open = text.IndexOf('(', StringComparison.Ordinal);
            string name = open < 0 ? text : text[..open];
            string number = open < 0 ? string.Empty : text[(open + 1)..^1];
            int token = number.Length == 0 ? 0 : int.Parse(number, CultureInfo.InvariantCulture);
            HuntOpKind kind = name switch
            {
                "Mint" => HuntOpKind.Mint,
                "Enroll" => HuntOpKind.Enroll,
                "EnrollAgain" => HuntOpKind.EnrollAgain,
                "PgDown" => HuntOpKind.PgDown,
                "PgUp" => HuntOpKind.PgUp,
                _ => HuntOpKind.RestartNodes,
            };
            ops.Add(new HuntOp(kind, token));
        }

        return ops;
    }

    /// <summary>An in-memory lab that answers the hunter's tools and models the product.</summary>
    private sealed class FakeLab
    {
        private readonly List<bool> _used = [];
        private bool _pgDown;
        private int _nodes;
        private int _idle;
        private int _staleIdle;
        private bool _healThrown;
        private int _queuedI1;
        private int _startI1;
        private bool _started;
        private int _checks;
        private int _caseChecks;
        private bool _flakeArmed;

        public bool ReusedTokenReturns201 { get; init; }

        public bool Pg500 { get; init; }

        public bool I1Violations { get; init; }

        public bool HealThrows { get; init; }

        public bool MintWithoutTokenRef { get; init; }

        /// <summary>When true, <c>svc_restart</c> answers an error result while the cut is active, so a call that is sent inside a cut makes the case fail (F104).</summary>
        public bool RestartFailsWhileCut { get; init; }

        /// <summary>Nodes' startup background services leave one idle pooled connection.</summary>
        public bool RestartWarm { get; init; }

        /// <summary>When non-zero, the warm-up enroll (hostname <c>hunt-warmup</c>) answers this status instead of the product's.</summary>
        public int WarmUpStatus { get; init; }

        /// <summary>
        /// When true, <c>fault_proxy_disable</c> answers an error result while an earlier disable has not been followed by
        /// <c>fault_heal</c> or <c>fault_reset</c>, as <c>FaultBook</c> does (F109).
        /// </summary>
        public bool DisableClashes { get; init; }

        /// <summary>
        /// A cut kills the idle pooled connections; the next enroll after heal that takes one answers 500 and queues one I1 for the
        /// next <c>invariants_check</c> (run 9's product behaviour).
        /// </summary>
        public bool StalePoolAfterCut { get; init; }

        /// <summary>The I1 violations queued before the first call; the first <c>invariants_check</c> returns them and clears them.</summary>
        public int StaleI1AtStart { get; init; }

        /// <summary>When n is set, the n-th <c>invariants_check</c> call returns one I1, and no other call does.</summary>
        public int FlakyI1OnCheck { get; init; }

        /// <summary>
        /// When true, the third after-op <c>invariants_check</c> of a case returns one I1, so a case with three checked ops diverges.
        /// That case arms one flake: the first after-op check of the case that runs next returns one I1, once. A shrink therefore
        /// fails on that next case and replays clean, while the original replays dirty. Shrink length is bounded by time, so
        /// the rule counts checks within a case, not cases.
        /// </summary>
        public bool I1FromThirdOp { get; init; }

        /// <summary>How many of the next enroll probes answer 429 before the product answers normally.</summary>
        public int Throttles { get; set; }

        public Action? OnFirstPgDown { get; set; }

        public List<(string Tool, bool Cancelled)> Calls { get; } = [];

        /// <summary>The arguments each call carried, so a test can see whether lease_id was injected.</summary>
        public List<(string Tool, JsonElement Args)> Arguments { get; } = [];

        public async Task<LabToolResult> InvokeAsync(string tool, JsonElement args, LabCallContext context, CancellationToken ct)
        {
            Calls.Add((tool, ct.IsCancellationRequested));
            Arguments.Add((tool, args.Clone()));
            ct.ThrowIfCancellationRequested();
            if (!_started)
            {
                _started = true;
                _startI1 = StaleI1AtStart;
            }

            await Task.Yield();
            switch (tool)
            {
                case "stack_reset_data":
                    _caseChecks = 0;
                    _used.Clear();
                    _nodes = 0;
                    _pgDown = false;
                    return Ok(new { });
                case "fault_reset":
                    if (HealThrows && _pgDown && !_healThrown)
                    {
                        // The first heal during a cut throws and leaves the cut open; the next reset (the next case's start) heals.
                        _healThrown = true;
                        throw new InvalidOperationException("heal failed");
                    }

                    Heal();
                    return Ok(new { healed = Array.Empty<string>() });
                case "nodes_token_mint":
                    if (MintWithoutTokenRef)
                    {
                        return Ok(new { });
                    }

                    // The warm-up mints before the case's data reset, which discards its token, so it is not held to the case's cap.
                    bool warmup = args.TryGetProperty("label", out JsonElement label) && label.GetString() == "hunt-warmup";
                    if (!warmup && _used.Count >= 8)
                    {
                        return Ok(new { });
                    }

                    _used.Add(false);
                    return Ok(new { token_ref = "ref-" + (_used.Count - 1).ToString(CultureInfo.InvariantCulture) });
                case "fault_proxy_disable":
                    if (DisableClashes && _pgDown)
                    {
                        return new LabToolResult(true, JsonSerializer.SerializeToElement(new { error = tool }));
                    }

                    bool first = !_pgDown;
                    _pgDown = true;
                    if (first)
                    {
                        if (StalePoolAfterCut)
                        {
                            _staleIdle += _idle;
                            _idle = 0;
                        }

                        OnFirstPgDown?.Invoke();
                    }

                    return Ok(new { fault_id = "f1" });
                case "fault_heal":
                    Heal();
                    return Ok(new { healed = true });
                case "svc_restart":
                    if (RestartFailsWhileCut && _pgDown)
                    {
                        return new LabToolResult(true, JsonSerializer.SerializeToElement(new { error = tool }));
                    }

                    _idle = RestartWarm ? 1 : 0;
                    _staleIdle = 0;
                    return Ok(new { });
                case "nodes_enroll_probe":
                    if (IsWarmUp(args))
                    {
                        return Ok(new { status = WarmUpStatus != 0 ? WarmUpStatus : Enroll(args) });
                    }

                    if (Throttles > 0)
                    {
                        Throttles--;
                        return Ok(new { status = 429, retry_after_s = 2 });
                    }

                    return Ok(new { status = Enroll(args) });
                case "db_query":
                    string sql = args.GetProperty("sql").GetString()!;
                    int n = sql.Contains("enrollment_tokens", StringComparison.Ordinal) ? _used.Count(used => used) : _nodes;
                    return Ok(new { rows = new[] { new { n } } });
                case "invariants_check":
                    // Queued I1s are returned once and then cleared; the rule and FlakyI1OnCheck add their own.
                    _checks++;
                    _caseChecks++;
                    int queued = _queuedI1 + _startI1;
                    _queuedI1 = 0;
                    _startI1 = 0;
                    // The first check of a case is its start checkpoint; only the after-op checks (2, 3, ...) can flake it.
                    int flaky = 0;
                    if (I1FromThirdOp && _caseChecks == 4)
                    {
                        flaky = 1;
                        _flakeArmed = true;
                    }
                    else if (I1FromThirdOp && _caseChecks == 2 && _flakeArmed)
                    {
                        flaky = 1;
                        _flakeArmed = false;
                    }

                    int count = queued + (I1Violations && !_pgDown ? 1 : 0) + (FlakyI1OnCheck == _checks ? 1 : 0) + flaky;
                    // The real InvariantViolation shape (F92): the id is invariant_id, never rule.
                    InvariantViolation[] violations = new InvariantViolation[count];
                    Array.Fill(violations, new InvariantViolation("I1", "nodes", "unhandled", "unhandled", DateTimeOffset.UnixEpoch, null));
                    return new LabToolResult(false, JsonSerializer.SerializeToElement(new
                    {
                        count = violations.Length,
                        violations,
                        errors = Array.Empty<object>(),
                        skipped = Array.Empty<object>(),
                    }, LabJson.Options));
                default:
                    return new LabToolResult(true, JsonSerializer.SerializeToElement(new { error = tool }));
            }
        }

        /// <summary>A heal ends the cut. The idle pool is not touched: a cut's dead connections stay dead until they are taken.</summary>
        private void Heal() => _pgDown = false;

        private static bool IsWarmUp(JsonElement args) =>
            args.TryGetProperty("hostname", out JsonElement hostname) && hostname.GetString() == "hunt-warmup";

        private int Enroll(JsonElement args)
        {
            if (_pgDown)
            {
                return Pg500 ? 500 : 503;
            }

            if (_staleIdle > 0)
            {
                // The first enroll that takes a dead pooled connection answers 500 and queues one I1 (StalePoolAfterCut).
                _staleIdle--;
                _queuedI1++;
                return 500;
            }

            int status = Admit(args);
            _idle = Math.Max(_idle, 1);
            return status;
        }

        private int Admit(JsonElement args)
        {
            int index = int.Parse(args.GetProperty("token_ref").GetString()!["ref-".Length..], CultureInfo.InvariantCulture);
            if (_used[index])
            {
                return ReusedTokenReturns201 ? 201 : 401;
            }

            _used[index] = true;
            _nodes++;
            return 201;
        }

        private static LabToolResult Ok(object payload) => new(false, JsonSerializer.SerializeToElement(payload));
    }
}
