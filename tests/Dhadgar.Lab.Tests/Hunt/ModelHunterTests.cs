using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Hunt;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Tools;
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
            int token = open < 0 ? 0 : int.Parse(text[(open + 1)..^1], CultureInfo.InvariantCulture);
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

        public bool ReusedTokenReturns201 { get; init; }

        public bool Pg500 { get; init; }

        public bool I1Violations { get; init; }

        public bool HealThrows { get; init; }

        public bool MintWithoutTokenRef { get; init; }

        /// <summary>How many of the next enroll probes answer 429 before the product answers normally.</summary>
        public int Throttles { get; set; }

        public Action? OnFirstPgDown { get; set; }

        public List<(string Tool, bool Cancelled)> Calls { get; } = [];

        public async Task<LabToolResult> InvokeAsync(string tool, JsonElement args, LabCallContext context, CancellationToken ct)
        {
            Calls.Add((tool, ct.IsCancellationRequested));
            ct.ThrowIfCancellationRequested();

            await Task.Yield();
            switch (tool)
            {
                case "stack_reset_data":
                    _used.Clear();
                    _nodes = 0;
                    _pgDown = false;
                    return Ok(new { });
                case "fault_reset":
                    if (HealThrows && _pgDown)
                    {
                        throw new InvalidOperationException("heal failed");
                    }

                    _pgDown = false;
                    return Ok(new { healed = Array.Empty<string>() });
                case "nodes_token_mint":
                    if (MintWithoutTokenRef)
                    {
                        return Ok(new { });
                    }

                    if (_used.Count >= 8)
                    {
                        return Ok(new { });
                    }

                    _used.Add(false);
                    return Ok(new { token_ref = "ref-" + (_used.Count - 1).ToString(CultureInfo.InvariantCulture) });
                case "fault_proxy_disable":
                    bool first = !_pgDown;
                    _pgDown = true;
                    if (first)
                    {
                        OnFirstPgDown?.Invoke();
                    }

                    return Ok(new { fault_id = "f1" });
                case "fault_heal":
                    _pgDown = false;
                    return Ok(new { healed = true });
                case "svc_restart":
                    return Ok(new { });
                case "nodes_enroll_probe":
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
                    // The real InvariantViolation shape (F92): the id is invariant_id, never rule.
                    InvariantViolation[] violations = I1Violations && !_pgDown
                        ? [new InvariantViolation("I1", "nodes", "unhandled", "unhandled", DateTimeOffset.UnixEpoch, null)]
                        : [];
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

        private int Enroll(JsonElement args)
        {
            if (_pgDown)
            {
                return Pg500 ? 500 : 503;
            }

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
