using System.Security.Cryptography;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Soak;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools.Soak;

/// <summary>
/// Arguments of <c>soak_run</c>. <see cref="Spec"/> is a path below <c>docs/guides/uat-specs/</c>; <see cref="Rows"/> are the
/// rows to loop (all rows when absent); <see cref="DurationS"/> is 300 to 86400.
/// </summary>
public sealed record SoakRunArgs(string? Spec = null, IReadOnlyList<string>? Rows = null, int? DurationS = null, ulong? Seed = null, string? LeaseId = null);

/// <summary>The <c>soak_run</c> payload.</summary>
public sealed record SoakStarted(string JobId, ulong Seed);

/// <summary>
/// <c>soak_run</c> (OwnLease): starts a soak of one spec for <c>duration_s</c> seconds (300 to 86400) and returns its
/// <c>job_id</c> and seed. The job loops the spec through <c>uat_run</c> and samples the labelled containers every 30 seconds
/// for I7. Only one soak runs at a time. The job takes its own lease; a caller's <c>lease_id</c> is checked by the dispatcher
/// and not used further.
/// </summary>
public sealed class SoakRunTool : LabTool<SoakRunArgs>
{
    /// <summary>The shortest soak, in seconds.</summary>
    public const int MinDurationS = 300;

    /// <summary>The longest soak, in seconds.</summary>
    public const int MaxDurationS = 86400;

    /// <summary>The most rows one soak loops.</summary>
    public const int MaxRows = 50;

    private const string SpecsPrefix = "docs/guides/uat-specs/";

    private readonly JobRunner _jobs;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly GatewayPacer _pacer;
    private readonly ResourceSampler _sampler;
    private readonly ResourceSampleStore _store;
    private readonly TimeProvider _time;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. The dispatcher is resolved at call time because the tool registry contains this tool and the
    /// dispatcher depends on the registry.
    /// </summary>
    public SoakRunTool(JobRunner jobs, LabPaths paths, IPathValidator validator, GatewayPacer pacer, ResourceSampler sampler,
        ResourceSampleStore store, TimeProvider time, IServiceProvider services)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "soak_run";

    /// <inheritdoc />
    public override string Description => "Starts a soak: loops a uat spec (docs/guides/uat-specs/<spec>; optional rows) through uat_run for duration_s (300 to 86400) seconds, sampling the lab containers' memory and process counts every 30 seconds for invariant I7. Returns job_id and seed; follow it with job_status.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(SoakRunArgs args, LabCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.IsNullOrWhiteSpace(args.Spec))
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] spec must name a uat spec below docs/guides/uat-specs/"));
        }

        string relative = args.Spec.StartsWith(SpecsPrefix, StringComparison.Ordinal) ? args.Spec[SpecsPrefix.Length..] : args.Spec;
        string specsRoot = Path.Combine(_paths.RepoRoot, "docs", "guides", "uat-specs");
        Result<string> spec = _validator.ValidatePath(Path.Combine(specsRoot, relative), [specsRoot]);
        if (spec.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] spec is outside docs/guides/uat-specs/"));
        }

        if (args.Rows is not null && (args.Rows.Count > MaxRows || args.Rows.Any(string.IsNullOrWhiteSpace)))
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] rows must be up to 50 non-empty row names"));
        }

        if (args.DurationS is not { } durationS || durationS < MinDurationS || durationS > MaxDurationS)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] duration_s must be 300 to 86400"));
        }

        if (_store.IsSoaking)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.Conflict] a soak is already running"));
        }

        ulong runSeed = args.Seed ?? BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        var run = new SoakRun(relative, args.Rows, durationS, runSeed);
        Result<JobHandle> started = _jobs.Start(SoakJob.Kind, runSeed, (jobContext, token) =>
            SoakJob.RunAsync(run, jobContext,
                (name, arguments, callContext, callToken) =>
                    _services.GetRequiredService<LabToolDispatcher>().InvokeAsync(name, arguments, callContext, callToken),
                _pacer, _sampler, _store, _time, token));
        return Task.FromResult(started.IsFailure
            ? Result<object>.Failure(started.Error)
            : Result<object>.Success(new SoakStarted(started.Value!.JobId, runSeed)));
    }
}
