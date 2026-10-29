using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsCheck;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Hunt;

/// <summary>
/// The enrollment model hunter (<c>hunt_model_run</c>, packet L27 items 4 and 5). A CsCheck generator draws 1 to 15 model ops
/// from the run seed; each iteration resets the nodes data, runs the ops through the lab tools with the job's lease, and checks
/// the product against <see cref="EnrollmentModel"/>. A divergence is shrunk by CsCheck and written to <c>repro\repro.toml</c>
/// with a row <c>hunt/enrollment</c> FAIL. Every op goes through the <c>invoke</c> delegate (the dispatcher in the daemon, a
/// fake in tests), so the hunter never calls the Nodes adapter directly (R-CONTRACT).
/// </summary>
public sealed class ModelHunter
{
    /// <summary>The job kind, also the bundle's <c>runs.kind</c>.</summary>
    public const string Kind = "hunt_model";

    /// <summary>The row section and id the hunter writes (A5).</summary>
    public const string RowSection = "hunt";

    /// <summary>The row id the hunter writes (A5).</summary>
    public const string RowId = "enrollment";

    /// <summary>The model name written to <c>repro.toml</c>.</summary>
    public const string ModelName = "enrollment";

    /// <summary>The heal budget of the <c>finally</c> that runs <c>fault_reset</c> (A3).</summary>
    public static readonly TimeSpan HealBudget = TimeSpan.FromSeconds(30);

    /// <summary>The wait after <c>PgDown</c> before the next op (A2: no readiness check for Postgres, F67).</summary>
    public static readonly TimeSpan DefaultSettle = TimeSpan.FromSeconds(2);

    private const string Proxy = "nodes-postgres";
    private const string Service = "nodes";
    private const string Database = "nodes";
    private const string Platform = "linux";
    private const string GatewayVia = "gateway";
    private const string AgentIngressVia = "agent_ingress";
    private const string NodesCountSql = "SELECT count(*) AS n FROM nodes";
    private const string UsedCountSql = "SELECT count(*) AS n FROM enrollment_tokens WHERE \"UsedAt\" IS NOT NULL";
    private const string ReproFolder = "repro";
    private const string ReproFile = "repro.toml";
    private const string RowsFolder = "rows";

    private static readonly Gen<HuntOp> OpGen = Gen.Select(
        Gen.Int[0, 5],
        Gen.Int[0, EnrollmentModel.MaxTokens - 1],
        (kind, token) => new HuntOp((HuntOpKind)kind, token));

    private readonly Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> _invoke;
    private readonly GatewayPacer _pacer;
    private readonly TimeProvider _time;
    private readonly IPathValidator _validator;
    private readonly string _leaseId;
    private readonly string _via;
    private readonly TimeSpan _settle;
    private readonly ILogger<ModelHunter> _logger;

    /// <summary>
    /// Creates the hunter.
    /// </summary>
    /// <param name="invoke">The tool call: the dispatcher's <c>InvokeAsync</c> in the daemon, a fake in tests.</param>
    /// <param name="pacer">The Gateway pacer (A1); every enroll through the Gateway waits on it.</param>
    /// <param name="time">The time provider for the heal budget and the settle wait.</param>
    /// <param name="validator">The path validator for the repro and row files.</param>
    /// <param name="leaseId">The job's own lease id. It goes only to the dispatcher and is never logged or written.</param>
    /// <param name="via">The enrollment route: <c>gateway</c> (default), <c>agent_ingress</c> or <c>nodes</c>.</param>
    /// <param name="settle">The wait after <c>PgDown</c>; the default is <see cref="DefaultSettle"/>.</param>
    /// <param name="logger">The logger for a heal that did not finish; logs no ids or arguments. Null uses <see cref="NullLogger"/>.</param>
    public ModelHunter(Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke, GatewayPacer pacer,
        TimeProvider time, IPathValidator validator, string leaseId, string via = GatewayVia, TimeSpan? settle = null,
        ILogger<ModelHunter>? logger = null)
    {
        _logger = logger ?? NullLogger<ModelHunter>.Instance;
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _leaseId = string.IsNullOrWhiteSpace(leaseId) ? throw new ArgumentException("a lease id is required", nameof(leaseId)) : leaseId;
        _via = via is GatewayVia or AgentIngressVia or "nodes" ? via : throw new ArgumentException("via must be gateway, agent_ingress or nodes", nameof(via));
        _settle = settle ?? DefaultSettle;
    }

    /// <summary>
    /// Runs the hunt: CsCheck draws up to <paramref name="iterations"/> op lists from the seed of <paramref name="runSeed"/>
    /// for at most <paramref name="budgetSeconds"/>. A divergence is shrunk, replayed once to report its first diverging op, and
    /// written to <paramref name="jobFolder"/> when one is given.
    /// </summary>
    /// <param name="runSeed">The run seed; the CsCheck seed string derives from it (R-SEED).</param>
    /// <param name="iterations">The number of iterations (at least 1).</param>
    /// <param name="budgetSeconds">The time budget of the sample, in seconds (at least 1).</param>
    /// <param name="jobFolder">The job folder for <c>repro\repro.toml</c> and the row, or null to write nothing.</param>
    /// <param name="buildId">The build id written to the repro.</param>
    /// <param name="ct">The job's cancellation token.</param>
    public async Task<Result<JobOutcome>> RunAsync(ulong runSeed, int iterations, int budgetSeconds, string? jobFolder, string buildId,
        CancellationToken ct)
    {
        if (iterations < 1 || budgetSeconds < 1)
        {
            return Result<JobOutcome>.Failure("[Lab.InvalidArgument] iterations and budget_s must be at least 1");
        }

        string cscheckSeed = new PCG(0u, runSeed).ToString();
        SampleFindings findings = await Task.Run(() => Sample(cscheckSeed, iterations, budgetSeconds, ct), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (findings.Infrastructure is not null)
        {
            return Result<JobOutcome>.Failure(findings.Infrastructure);
        }

        if (findings.Shrunk is null)
        {
            var passed = new JsonObject { ["state"] = "passed", ["iterations"] = iterations, ["throttled"] = findings.Throttled, ["known"] = findings.Known };
            return Result<JobOutcome>.Success(new JobOutcome("pass", passed));
        }

        // Replay the shrunk case once: the first diverging op and the observed and expected values.
        CaseResult replay = await RunCaseAsync(findings.Shrunk, ct).ConfigureAwait(false);
        if (replay.Infrastructure is not null)
        {
            return Result<JobOutcome>.Failure(replay.Infrastructure);
        }

        string detail = replay.Detail ?? "the shrunk case did not diverge on replay";
        var ops = new JsonArray(findings.Shrunk.Select(op => (JsonNode)JsonValue.Create(op.ToString())!).ToArray());
        var failed = new JsonObject
        {
            ["state"] = replay.Diverged ? "diverged" : "not_reproduced",
            ["model"] = ModelName,
            ["run_seed"] = (long)runSeed,
            ["cscheck_seed"] = cscheckSeed,
            ["ops"] = ops,
            ["first_diverging_op"] = replay.Index,
            ["detail"] = detail,
            ["known"] = findings.Known,
        };
        if (jobFolder is not null)
        {
            Result written = await WriteReproAsync(jobFolder, runSeed, cscheckSeed, buildId, findings.Shrunk, replay.Index, detail, ct)
                .ConfigureAwait(false);
            if (written.IsFailure)
            {
                return Result<JobOutcome>.Failure(written.Error);
            }
        }

        return Result<JobOutcome>.Success(new JobOutcome("fail", failed));
    }

    /// <summary>
    /// Replays one op list exactly once. <see cref="CaseResult.Diverged"/> says whether the failure reproduces.
    /// </summary>
    /// <param name="ops">The ops, in order.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<Result<CaseResult>> ReplayAsync(IReadOnlyList<HuntOp> ops, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ops);
        CaseResult result = await RunCaseAsync(ops, ct).ConfigureAwait(false);
        return result.Infrastructure is not null ? Result<CaseResult>.Failure(result.Infrastructure) : Result<CaseResult>.Success(result);
    }

    /// <summary>The findings of one CsCheck sample.</summary>
    private sealed record SampleFindings(List<HuntOp>? Shrunk, int Throttled, int Known, string? Infrastructure);

    /// <summary>
    /// The CsCheck sample. CsCheck calls the assertion synchronously, so each case blocks on its async tool calls; the caller
    /// runs this on a worker thread. Infrastructure errors are kept and end the sample without a shrink.
    /// </summary>
    private SampleFindings Sample(string cscheckSeed, int iterations, int budgetSeconds, CancellationToken ct)
    {
        List<HuntOp>? captured = null;
        int throttled = 0;
        int known = 0;
        string? infrastructure = null;
        try
        {
            Check.Sample(OpGen.List[1, 15],
                ops =>
                {
                    if (infrastructure is not null)
                    {
                        return;
                    }

                    CaseResult result = RunCaseAsync(ops, ct).GetAwaiter().GetResult();
                    if (result.Infrastructure is not null)
                    {
                        infrastructure = result.Infrastructure;
                        return;
                    }

                    throttled += result.Throttled ? 1 : 0;
                    known += result.Known;
                    if (result.Diverged)
                    {
                        throw new InvalidOperationException(result.Detail ?? "diverged");
                    }
                },
                _ => { },
                cscheckSeed,
                iterations,
                budgetSeconds,
                1,
                ops =>
                {
                    // CsCheck prints the shrunk failing input last; keep it.
                    captured = [.. ops];
                    return string.Join(", ", ops.Select(op => op.ToString()));
                },
                null);
        }
        catch (CsCheckException)
        {
            return new SampleFindings(captured, throttled, known, infrastructure);
        }

        return new SampleFindings(null, throttled, known, infrastructure);
    }

    /// <summary>The result of one case.</summary>
    /// <param name="Diverged">True when the product disagreed with the model.</param>
    /// <param name="Index">The index of the first diverging op, or -1.</param>
    /// <param name="Detail">What diverged, with the observed and expected values.</param>
    /// <param name="Throttled">True when a Gateway enroll was throttled twice; the case ends and is not a failure.</param>
    /// <param name="Known">The count of 5xx answers to an enroll while Postgres was cut (KI-F67, known).</param>
    /// <param name="Infrastructure">An infrastructure error (a tool that failed to run), or null.</param>
    public sealed record CaseResult(bool Diverged, int Index, string? Detail, bool Throttled, int Known, string? Infrastructure)
    {
        /// <summary>A case that matched the model.</summary>
        public static CaseResult Passed(int known) => new(false, -1, null, false, known, null);
    }

    /// <summary>Runs one case: resets the data, runs each op, checks the product after each op, and heals in <c>finally</c> (A3).</summary>
    private async Task<CaseResult> RunCaseAsync(IReadOnlyList<HuntOp> ops, CancellationToken ct)
    {
        var model = new EnrollmentModel();
        var tokenRefs = new List<string>();
        string? faultId = null;
        bool ranPgDown = false;
        int known = 0;
        try
        {
            Result<JsonElement> reset = await CallAsync("stack_reset_data", new JsonObject { ["databases"] = new JsonArray(Database) }, ct).ConfigureAwait(false);
            if (reset.IsFailure)
            {
                return Infrastructure(reset.Error);
            }

            reset = await CallAsync("fault_reset", new JsonObject(), ct).ConfigureAwait(false);
            if (reset.IsFailure)
            {
                return Infrastructure(reset.Error);
            }

            for (int i = 0; i < ops.Count; i++)
            {
                HuntOp op = ops[i];
                HuntExpectation expected = model.Expect(op);
                bool enroll = op.Kind is HuntOpKind.Enroll or HuntOpKind.EnrollAgain;
                if (enroll && expected == HuntExpectation.None)
                {
                    continue;
                }

                string? mismatch = null;
                switch (op.Kind)
                {
                    case HuntOpKind.Mint:
                        Result<JsonElement> mint = await CallAsync("nodes_token_mint",
                            new JsonObject { ["label"] = "hunt", ["validity_minutes"] = HuntOp.ValidityMinutes }, ct).ConfigureAwait(false);
                        if (mint.IsFailure)
                        {
                            return Infrastructure(mint.Error);
                        }

                        if (model.Tokens.Count < EnrollmentModel.MaxTokens)
                        {
                            if (Text(mint.Value, "token_ref") is not { } tokenRef)
                            {
                                return Infrastructure("[Lab.Unavailable] nodes_token_mint returned no token_ref");
                            }

                            tokenRefs.Add(tokenRef);
                        }

                        break;
                    case HuntOpKind.Enroll:
                    case HuntOpKind.EnrollAgain:
                        if (_via is GatewayVia or AgentIngressVia)
                        {
                            Result paced = await _pacer.WaitAsync(ct).ConfigureAwait(false);
                            if (paced.IsFailure)
                            {
                                return Infrastructure(paced.Error);
                            }
                        }

                        if (op.Token < 0 || op.Token >= tokenRefs.Count)
                        {
                            return Infrastructure("[Lab.Unavailable] hunt op names a token that was not minted");
                        }

                        var probe = new JsonObject
                        {
                            ["token_ref"] = tokenRefs[op.Token],
                            ["platform"] = Platform,
                            ["via"] = _via,
                        };
                        Result<JsonElement> enrolled = await ProbeEnrollAsync(probe, ct).ConfigureAwait(false);
                        if (enrolled.IsFailure)
                        {
                            return Infrastructure(enrolled.Error);
                        }

                        int status = Number(enrolled.Value, "status");
                        if (status == 429)
                        {
                            return new CaseResult(false, -1, null, true, known, null);
                        }

                        if (expected == HuntExpectation.NotSuccess && status >= 500)
                        {
                            known++;
                        }

                        mismatch = Compare(expected, status);
                        break;
                    case HuntOpKind.PgDown:
                        ranPgDown = true;
                        Result<JsonElement> cut = await CallAsync("fault_proxy_disable", new JsonObject { ["proxy"] = Proxy }, ct).ConfigureAwait(false);
                        if (cut.IsFailure)
                        {
                            return Infrastructure(cut.Error);
                        }

                        faultId = Text(cut.Value, "fault_id") ?? faultId;
                        if (_settle > TimeSpan.Zero)
                        {
                            await Task.Delay(_settle, _time, ct).ConfigureAwait(false);
                        }

                        break;
                    case HuntOpKind.PgUp:
                        if (faultId is not null)
                        {
                            Result<JsonElement> healed = await CallAsync("fault_heal", new JsonObject { ["fault_id"] = faultId }, ct).ConfigureAwait(false);
                            if (healed.IsFailure)
                            {
                                return Infrastructure(healed.Error);
                            }

                            faultId = null;
                        }

                        break;
                    case HuntOpKind.RestartNodes:
                        Result<JsonElement> restarted = await CallAsync("svc_restart", new JsonObject { ["service"] = Service }, ct).ConfigureAwait(false);
                        if (restarted.IsFailure)
                        {
                            return Infrastructure(restarted.Error);
                        }

                        break;
                }

                model.Apply(op, expected);
                mismatch ??= await CheckStateAsync(model, op, ct).ConfigureAwait(false);
                if (mismatch is not null)
                {
                    return new CaseResult(true, i, "op " + i.ToString(CultureInfo.InvariantCulture) + " " + op + ": " + mismatch, false, known, null);
                }
            }

            return CaseResult.Passed(known);
        }
        finally
        {
            if (ranPgDown)
            {
                await HealAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>After each op: the DB agrees with the model, and no I3 violation and no I1 outside a cut window on enroll (A2).</summary>
    private async Task<string?> CheckStateAsync(EnrollmentModel model, HuntOp op, CancellationToken ct)
    {
        Result<JsonElement> nodes = await CallAsync("db_query", new JsonObject
        {
            ["database"] = "nodes",
            ["sql"] = NodesCountSql,
            ["max_rows"] = 1,
        }, ct).ConfigureAwait(false);
        if (nodes.IsFailure)
        {
            return "db_query failed";
        }

        Result<JsonElement> used = await CallAsync("db_query", new JsonObject
        {
            ["database"] = "nodes",
            ["sql"] = UsedCountSql,
            ["max_rows"] = 1,
        }, ct).ConfigureAwait(false);
        if (used.IsFailure)
        {
            return "db_query failed";
        }

        int dbNodes = FirstRowNumber(nodes.Value);
        int dbUsed = FirstRowNumber(used.Value);
        if (dbNodes != model.Nodes || dbUsed != model.UsedTokens)
        {
            return "db has " + dbNodes.ToString(CultureInfo.InvariantCulture) + " nodes and " + dbUsed.ToString(CultureInfo.InvariantCulture)
                + " used tokens, model expects " + model.Nodes.ToString(CultureInfo.InvariantCulture) + " and "
                + model.UsedTokens.ToString(CultureInfo.InvariantCulture);
        }

        Result<JsonElement> invariants = await CallAsync("invariants_check", new JsonObject(), ct).ConfigureAwait(false);
        if (invariants.IsFailure)
        {
            return "invariants_check failed";
        }

        if (invariants.Value.TryGetProperty("violations", out JsonElement violations) && violations.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement violation in violations.EnumerateArray())
            {
                // The shape is invariants_check's InvariantViolation: the id is invariant_id (F92).
                string invariantId = Text(violation, "invariant_id") ?? string.Empty;
                if (invariantId.StartsWith("I3", StringComparison.Ordinal))
                {
                    return "invariant " + invariantId + " violated";
                }

                bool allowed = model.PgDown && op.Kind == HuntOpKind.Enroll;
                if (invariantId.StartsWith("I1", StringComparison.Ordinal) && !allowed)
                {
                    return "invariant " + invariantId + " violated outside a PgDown window";
                }
            }
        }

        return null;
    }

    /// <summary>The heal in <c>finally</c>: <c>fault_reset</c> under its own budget, never the job's token (A3).</summary>
    private async Task HealAsync()
    {
        try
        {
            using var budget = new CancellationTokenSource(HealBudget, _time);
            await CallAsync("fault_reset", new JsonObject(), budget.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A heal that throws must not replace the case's own result. Only the fact is logged: no ids, no arguments.
            _logger.LogWarning("hunt heal did not finish");
        }
    }

    /// <summary>
    /// One enroll probe. A 429 waits the probe's <c>retry_after_s</c> (clamped to 1 to 60 s, else 60 s), paces again, and
    /// probes once more with the same arguments (R2-N6). A second 429 is the caller's throttled case.
    /// </summary>
    private async Task<Result<JsonElement>> ProbeEnrollAsync(JsonObject probe, CancellationToken ct)
    {
        Result<JsonElement> first = await CallAsync("nodes_enroll_probe", probe, ct).ConfigureAwait(false);
        if (first.IsFailure || Number(first.Value, "status") != 429)
        {
            return first;
        }

        await Task.Delay(RetryWait(first.Value), _time, ct).ConfigureAwait(false);
        if (_via is GatewayVia or AgentIngressVia)
        {
            Result paced = await _pacer.WaitAsync(ct).ConfigureAwait(false);
            if (paced.IsFailure)
            {
                return Result<JsonElement>.Failure(paced.Error);
            }
        }

        return await CallAsync("nodes_enroll_probe", probe, ct).ConfigureAwait(false);
    }

    private static TimeSpan RetryWait(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("retry_after_s", out JsonElement value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60))
            : TimeSpan.FromSeconds(60);

    private async Task<Result<JsonElement>> CallAsync(string tool, JsonObject args, CancellationToken ct)
    {
        // An Open tool's schema rejects lease_id (db_query, invariants_check answer Lab.InvalidArgument). Only a
        // Leased or OwnLease tool takes the hunt's lease.
        if (ToolPolicy.GateFor(tool) != LabGate.Open)
        {
            args["lease_id"] = _leaseId;
        }


        JsonElement arguments = JsonSerializer.SerializeToElement(args);
        var context = new LabCallContext(Guid.NewGuid().ToString("N"), _leaseId, "job:hunt");
        LabToolResult call = await _invoke(tool, arguments, context, ct).ConfigureAwait(false);
        return call.IsError
            ? Result<JsonElement>.Failure("[Lab.Unavailable] " + tool + " did not complete")
            : Result<JsonElement>.Success(call.Payload);
    }

    private static CaseResult Infrastructure(string error) => new(false, -1, null, false, 0, error);

    private static string? Compare(HuntExpectation expected, int status) => expected switch
    {
        HuntExpectation.Created when status != 201 => "expected 201, got " + status.ToString(CultureInfo.InvariantCulture),
        HuntExpectation.Unauthorized when status != 401 => "expected 401, got " + status.ToString(CultureInfo.InvariantCulture),
        HuntExpectation.NotSuccess when status is >= 200 and < 300 => "expected no 2xx while Postgres is cut, got " + status.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)
            ? number
            : 0;

    /// <summary>The first row's <c>n</c> of a <c>db_query</c> payload (<c>{"rows":[{"n":...}]}</c>), or 0.</summary>
    private static int FirstRowNumber(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("rows", out JsonElement rows)
            && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0)
        {
            return Number(rows[0], "n");
        }

        return 0;
    }

    /// <summary>Writes <c>repro\repro.toml</c> and the <c>hunt/enrollment</c> FAIL row under the job folder, both validated.</summary>
    private async Task<Result> WriteReproAsync(string jobFolder, ulong runSeed, string cscheckSeed, string buildId,
        IReadOnlyList<HuntOp> ops, int index, string detail, CancellationToken ct)
    {
        var toml = new StringBuilder();
        toml.Append("kind = \"hunt\"\n");
        toml.Append("model = \"").Append(ModelName).Append("\"\n");
        toml.Append("run_seed = ").Append(runSeed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        toml.Append("cscheck_seed = \"").Append(cscheckSeed).Append("\"\n");
        toml.Append("build_id = \"").Append(buildId).Append("\"\n");
        toml.Append("ops = [").Append(string.Join(", ", ops.Select(op => "\"" + op + "\""))).Append("]\n");
        toml.Append("first_diverging_op = ").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
        toml.Append("detail = \"").Append(detail.Replace('"', '\'')).Append("\"\n");

        string reproFile = Path.Combine(jobFolder, ReproFolder, ReproFile);
        Result<string> reproPath = _validator.ValidatePath(reproFile, [jobFolder]);
        if (reproPath.IsFailure)
        {
            return Result.Failure(reproPath.Error);
        }

        string rowFile = Path.Combine(jobFolder, RowsFolder, RowSection, RowId + ".json");
        Result<string> rowPath = _validator.ValidatePath(rowFile, [jobFolder]);
        if (rowPath.IsFailure)
        {
            return Result.Failure(rowPath.Error);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(reproPath.Value!)!);
        Directory.CreateDirectory(Path.GetDirectoryName(rowPath.Value!)!);
        await File.WriteAllTextAsync(reproPath.Value!, toml.ToString(), ct).ConfigureAwait(false);
        var row = new JsonObject { ["section"] = RowSection, ["id"] = RowId, ["grade"] = "FAIL" };
        await File.WriteAllTextAsync(rowPath.Value!, row.ToJsonString(), ct).ConfigureAwait(false);
        return Result.Success();
    }
}
