using System.Security.Cryptography;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Chaos;
using Dhadgar.Lab.Jobs;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools.Chaos;

/// <summary>
/// Arguments of <c>chaos_run</c>. <see cref="Plan"/> is a plan id under <c>docs/guides/uat-specs/chaos/</c>;
/// <see cref="DurationS"/> is 60 to 14400; <see cref="Replay"/> is the job id of an earlier chaos job, whose seed is reused.
/// </summary>
public sealed record ChaosRunArgs(string? Plan = null, int? DurationS = null, ulong? Seed = null, string? Replay = null, string? LeaseId = null);

/// <summary>The <c>chaos_run</c> payload.</summary>
public sealed record ChaosStarted(string JobId, ulong Seed);

/// <summary>
/// <c>chaos_run</c> (OwnLease): starts a chaos job for one plan and returns its <c>job_id</c> and seed. The seed is the given
/// <c>seed</c>, the seed of the <c>replay</c> job, or a new random seed. The job takes its own lease; a caller's
/// <c>lease_id</c> is checked by the dispatcher and not used further. Only the plan's C4 fault tools run (R-BLAST).
/// </summary>
public sealed class ChaosRunTool : LabTool<ChaosRunArgs>
{
    /// <summary>The shortest run, in seconds.</summary>
    public const int MinDurationS = 60;

    /// <summary>The longest run, in seconds.</summary>
    public const int MaxDurationS = 14400;

    private readonly JobRunner _jobs;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly GatewayPacer _pacer;
    private readonly TimeProvider _time;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. The dispatcher is resolved at call time because the tool registry contains this tool and the
    /// dispatcher depends on the registry.
    /// </summary>
    public ChaosRunTool(JobRunner jobs, LabPaths paths, IPathValidator validator, GatewayPacer pacer, TimeProvider time,
        IServiceProvider services)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "chaos_run";

    /// <inheritdoc />
    public override string Description => "Starts a seeded chaos run of one plan (docs/guides/uat-specs/chaos/<plan>.toml): loops its workload and injects its C4 faults on the seeded timeline for duration_s (60 to 14400) seconds, then heals every fault. replay=<job_id> of a chaos job reuses its seed. Returns job_id and seed; follow it with job_status.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(ChaosRunArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.IsNullOrWhiteSpace(args.Plan) || !ChaosPlanLoader.IdPattern.IsMatch(args.Plan))
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] plan must be a plan id of lowercase letters, digits and hyphens"));
        }

        if (args.DurationS is not { } durationS || durationS < MinDurationS || durationS > MaxDurationS)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] duration_s must be 60 to 14400"));
        }

        ulong? seed = args.Seed;
        if (args.Replay is not null)
        {
            Result<JobRecord> earlier = _jobs.Get(args.Replay);
            if (earlier.IsFailure)
            {
                return Task.FromResult(Result<object>.Failure(earlier.Error));
            }

            if (earlier.Value!.Kind != ChaosJob.Kind)
            {
                return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] replay must name a chaos job"));
            }

            if (seed is not null && seed != earlier.Value.Seed)
            {
                return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] seed differs from the replayed job's seed"));
            }

            seed = earlier.Value.Seed;
        }

        string chaosRoot = Path.Combine(_paths.RepoRoot, "docs", "guides", "uat-specs", "chaos");
        Result<ChaosPlan> plan = ChaosPlanLoader.LoadFile(Path.Combine(chaosRoot, args.Plan + ".toml"), chaosRoot, _validator);
        if (plan.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(plan.Error));
        }

        ulong runSeed = seed ?? BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        ChaosRun run = ChaosJob.Prepare(plan.Value!, runSeed, durationS);
        Result<JobHandle> started = _jobs.Start(ChaosJob.Kind, runSeed, (jobContext, token) =>
            ChaosJob.RunAsync(run, jobContext,
                (name, arguments, callContext, callToken) =>
                    _services.GetRequiredService<LabToolDispatcher>().InvokeAsync(name, arguments, callContext, callToken),
                _pacer, _time, _validator, token));
        return Task.FromResult(started.IsFailure
            ? Result<object>.Failure(started.Error)
            : Result<object>.Success(new ChaosStarted(started.Value!.JobId, runSeed)));
    }
}
