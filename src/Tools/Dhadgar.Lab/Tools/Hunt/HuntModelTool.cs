using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Hunt;
using Dhadgar.Lab.Jobs;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools.Hunt;

/// <summary>
/// Arguments of <c>hunt_model_run</c>. <c>Model</c> must be <c>enrollment</c>; <c>Seed</c> defaults to a fresh random seed that
/// the job records; <c>Iterations</c> defaults to 25 (max 500); <c>BudgetS</c> defaults to 600 (max 3600); <c>Replay</c> is a path
/// under the jobs folder to a <c>repro.toml</c> written by an earlier hunt. <c>LeaseId</c> is accepted for the envelope and not used.
/// </summary>
public sealed record HuntModelArgs(
    string? Model = null,
    ulong? Seed = null,
    int? Iterations = null,
    int? BudgetS = null,
    string? Replay = null,
    string? LeaseId = null);

/// <summary>The <c>hunt_model_run</c> payload: the job, and the seed it runs with.</summary>
public sealed record HuntModelStarted(string JobId, ulong Seed);

/// <summary>
/// <c>hunt_model_run</c> (OwnLease): starts a model-based enrollment hunt (<see cref="ModelHunter"/>) as a job and returns its
/// <c>job_id</c> and seed. With <c>replay</c>, the job runs the recorded ops of one <c>repro.toml</c> exactly once instead. The
/// job takes its own lease, <c>job/&lt;job_id&gt;</c>, and the hunter uses that lease only.
/// </summary>
public sealed class HuntModelTool : LabTool<HuntModelArgs>
{
    /// <summary>The only model this tool hunts.</summary>
    public const string ModelName = "enrollment";

    /// <summary>The default iteration count.</summary>
    public const int DefaultIterations = 25;

    /// <summary>The maximum iteration count.</summary>
    public const int MaxIterations = 500;

    /// <summary>The default time budget, in seconds.</summary>
    public const int DefaultBudgetSeconds = 600;

    /// <summary>The maximum time budget, in seconds.</summary>
    public const int MaxBudgetSeconds = 3600;

    private readonly JobRunner _jobs;
    private readonly GatewayPacer _pacer;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly TimeProvider _time;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. The dispatcher is resolved at call time because the tool registry contains this tool and the
    /// dispatcher depends on the registry.
    /// </summary>
    public HuntModelTool(JobRunner jobs, GatewayPacer pacer, LabPaths paths, IPathValidator validator, TimeProvider time, IServiceProvider services)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "hunt_model_run";

    /// <inheritdoc />
    public override string Description => "Starts a seeded model-based enrollment hunt (model enrollment, iterations 1 to 500, default 25; budget_s 1 to 3600, default 600) as a job. Returns job_id and seed; follow it with job_status. With replay (a path under the jobs folder to a repro.toml) the job replays the recorded ops once and reports whether the failure reproduces.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(HuntModelArgs args, LabCallContext context, CancellationToken ct)
    {
        string model = args.Model ?? ModelName;
        if (model != ModelName)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] model must be enrollment"));
        }

        int iterations = args.Iterations ?? DefaultIterations;
        int budget = args.BudgetS ?? DefaultBudgetSeconds;
        if (iterations < 1 || iterations > MaxIterations)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] iterations must be 1 to 500"));
        }

        if (budget < 1 || budget > MaxBudgetSeconds)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] budget_s must be 1 to 3600"));
        }

        string buildId = LastBuildReader.Read(_paths, _validator).Id;
        Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke = (name, arguments, callContext, callToken) =>
            _services.GetRequiredService<LabToolDispatcher>().InvokeAsync(name, arguments, callContext, callToken);

        if (args.Replay is not null)
        {
            return ReplayAsync(args.Replay, invoke, buildId, ct);
        }

        ulong seed = args.Seed ?? NewSeed();
        Result<JobHandle> started = _jobs.Start(ModelHunter.Kind, seed, (jobContext, token) =>
            new ModelHunter(invoke, _pacer, _time, _validator, jobContext.LeaseId)
                .RunAsync(seed, iterations, budget, jobContext.Folder, buildId, token));
        return Task.FromResult(started.IsFailure
            ? Result<object>.Failure(started.Error)
            : Result<object>.Success(new HuntModelStarted(started.Value!.JobId, seed)));
    }

    /// <summary>Reads the recorded ops from a repro under the jobs folder and starts the replay job.</summary>
    private async Task<Result<object>> ReplayAsync(string replay, Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke, string buildId, CancellationToken ct)
    {
        Result<string> reproPath = _validator.ValidatePath(Path.Combine(_paths.Jobs, replay), [_paths.Jobs]);
        if (reproPath.IsFailure)
        {
            return Result<object>.Failure(reproPath.Error);
        }

        if (!File.Exists(reproPath.Value!))
        {
            return Result<object>.Failure("[Lab.NotFound] repro not found");
        }

        string text = await File.ReadAllTextAsync(reproPath.Value!, ct).ConfigureAwait(false);
        Result<ReproRecord> record = ReproParser.Parse(text);
        if (record.IsFailure)
        {
            return Result<object>.Failure(record.Error);
        }

        ReproRecord repro = record.Value!;
        GatewayPacer pacer = _pacer;
        TimeProvider time = _time;
        IPathValidator validator = _validator;
        Result<JobHandle> started = _jobs.Start(ModelHunter.Kind, repro.RunSeed, (jobContext, token) =>
            RunReplayAsync(repro.Ops, invoke, pacer, time, validator, jobContext.LeaseId, token));
        return started.IsFailure
            ? Result<object>.Failure(started.Error)
            : Result<object>.Success(new HuntModelStarted(started.Value!.JobId, repro.RunSeed));
    }

    /// <summary>The replay job: runs the recorded ops once. A reproduced failure is <c>fail</c>; otherwise <c>pass</c>.</summary>
    private static async Task<Result<JobOutcome>> RunReplayAsync(IReadOnlyList<HuntOp> ops,
        Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke, GatewayPacer pacer,
        TimeProvider time, IPathValidator validator, string leaseId, CancellationToken token)
    {
        var hunter = new ModelHunter(invoke, pacer, time, validator, leaseId);
        Result<ModelHunter.CaseResult> replay = await hunter.ReplayAsync(ops, token).ConfigureAwait(false);
        if (replay.IsFailure)
        {
            return Result<JobOutcome>.Failure(replay.Error);
        }

        ModelHunter.CaseResult result = replay.Value!;
        var payload = new JsonObject
        {
            ["state"] = result.Diverged ? "reproduced" : "not_reproduced",
            ["first_diverging_op"] = result.Index,
            ["detail"] = result.Detail,
            ["throttled"] = result.Throttled,
            ["known"] = result.Known,
        };
        return Result<JobOutcome>.Success(new JobOutcome(result.Diverged ? "fail" : "pass", payload));
    }

    private static ulong NewSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    /// <summary>The fields of a repro that a replay needs.</summary>
    /// <param name="RunSeed">The run seed the repro records (0 when absent).</param>
    /// <param name="Ops">The recorded ops, in order.</param>
    internal sealed record ReproRecord(ulong RunSeed, IReadOnlyList<HuntOp> Ops);

    /// <summary>Parses the <c>run_seed</c> and <c>ops</c> lines of a <c>repro.toml</c> written by <see cref="ModelHunter"/>.</summary>
    internal static class ReproParser
    {
        /// <summary>The longest op list a repro may hold (the generator draws 1 to 15 ops).</summary>
        public const int MaxOps = 15;

        private static readonly Regex OpsLine = new(@"^ops\s*=\s*\[(?<items>[^\]]*)\]\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex RunSeedLine = new(@"^run_seed\s*=\s*(?<seed>\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex QuotedItem = new("\"(?<op>[^\"]*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex OpItem = new(@"^(?<kind>Mint|Enroll|EnrollAgain|PgDown|PgUp|RestartNodes)(\((?<arg>\d+)\))?$", RegexOptions.CultureInvariant);

        /// <summary>Parses the repro text. An unknown op, a token index out of range or a bad count fails.</summary>
        public static Result<ReproRecord> Parse(string text)
        {
            Match opsMatch = OpsLine.Match(text);
            if (!opsMatch.Success)
            {
                return Result<ReproRecord>.Failure("[Lab.InvalidArgument] repro has no ops line");
            }

            var ops = new List<HuntOp>();
            foreach (Match item in QuotedItem.Matches(opsMatch.Groups["items"].Value))
            {
                Match op = OpItem.Match(item.Groups["op"].Value);
                if (!op.Success)
                {
                    return Result<ReproRecord>.Failure("[Lab.InvalidArgument] repro has an unknown op");
                }

                string kind = op.Groups["kind"].Value;
                int arg = op.Groups["arg"].Success ? int.Parse(op.Groups["arg"].Value, CultureInfo.InvariantCulture) : 0;
                if ((kind == "Enroll" || kind == "EnrollAgain") && arg >= EnrollmentModel.MaxTokens)
                {
                    return Result<ReproRecord>.Failure("[Lab.InvalidArgument] repro has a token index out of range");
                }

                ops.Add(new HuntOp(Enum.Parse<HuntOpKind>(kind), arg));
            }

            if (ops.Count < 1 || ops.Count > MaxOps)
            {
                return Result<ReproRecord>.Failure("[Lab.InvalidArgument] repro must hold 1 to 15 ops");
            }

            ulong runSeed = 0;
            Match seedMatch = RunSeedLine.Match(text);
            if (seedMatch.Success && !ulong.TryParse(seedMatch.Groups["seed"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out runSeed))
            {
                return Result<ReproRecord>.Failure("[Lab.InvalidArgument] repro run_seed is out of range");
            }

            return Result<ReproRecord>.Success(new ReproRecord(runSeed, ops));
        }
    }
}
