using System.Globalization;
using System.Security.Cryptography;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Fuzz;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools.Fuzz;

/// <summary>
/// Arguments of <c>fuzz_run</c>. <c>Target</c> defaults to <c>enroll</c> (<c>all</c> means the same, until L38 and L44 add
/// targets); <c>Seed</c> defaults to a fresh random seed the payload returns; <c>Cases</c> defaults to 200 (max 5000);
/// <c>Replay</c> is a case index that runs that one case alone (use the seed of the failing row). <c>LeaseId</c> is accepted for the envelope and not used.
/// </summary>
public sealed record FuzzRunArgs(
    string? Target = null,
    ulong? Seed = null,
    int? Cases = null,
    int? Replay = null,
    string? LeaseId = null);

/// <summary>The <c>fuzz_run</c> payload: the job and the seed it runs with.</summary>
public sealed record FuzzRunStarted(string JobId, ulong Seed);

/// <summary>
/// <c>fuzz_run</c> (OwnLease, C4): starts a seeded contract fuzz run of one target as a <c>fuzz</c> job and returns its
/// <c>job_id</c> and seed. The job takes its own lease and waits on the shared <see cref="GatewayPacer"/>; a 50-case run on
/// <c>enroll</c> takes about two minutes. The known-issue list is loaded from the repo root and must load, or the run is refused.
/// </summary>
public sealed class FuzzRunTool : LabTool<FuzzRunArgs>
{
    /// <summary>The relative path of the shipped known-issue list under the repo root.</summary>
    private static readonly string[] KnownIssuesPath = ["docs", "guides", "uat-specs", "known-issues.toml"];

    private readonly JobRunner _jobs;
    private readonly GatewayPacer _pacer;
    private readonly IFuzzTransport _transport;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly TimeProvider _time;
    private readonly IServiceProvider _services;

    /// <summary>Creates the tool over the job runner, the shared pacer, the transport, the validated paths and the services the leak check and the invariants call need.</summary>
    public FuzzRunTool(JobRunner jobs, GatewayPacer pacer, IFuzzTransport transport, LabPaths paths, IPathValidator validator, TimeProvider time,
        IServiceProvider services)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// The leak predicate for fuzz bodies (R2-N9, S-N3): the vault's values and the daemon token, each checked without copying
    /// the secret into a string or an array. The body is the only string the check reads.
    /// </summary>
    /// <param name="vault">The secret vault.</param>
    /// <param name="edge">The edge state that holds the daemon token.</param>
    internal static Func<string, bool> LeakCheck(SecretVault vault, EdgeState edge)
    {
        var scanner = new SecretScanner(vault);
        Func<string, bool> token = InvariantWiring.DaemonTokenPredicate(edge);
        return body => scanner.HasSecret(body) || token(body);
    }

    /// <inheritdoc />
    public override string Name => "fuzz_run";

    /// <inheritdoc />
    public override string Description => "Starts a seeded contract fuzz run (target enroll or all; cases 1 to 5000, default 200) as a job. Returns job_id and seed; follow it with job_status. With replay (a case index) the job runs that one case alone; pass the seed of the failing row.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(FuzzRunArgs args, LabCallContext context, CancellationToken ct)
    {
        string targetName = args.Target is null || string.Equals(args.Target, "all", StringComparison.Ordinal) ? FuzzTargets.Enroll.Name : args.Target;
        Result<FuzzTarget> target = FuzzTargets.Find(targetName);
        if (target.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(target.Error));
        }

        int cases = args.Cases ?? FuzzJob.DefaultCases;
        if (cases < 1 || cases > FuzzJob.MaxCases)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] cases must be 1 to 5000"));
        }

        int first = 0;
        if (args.Replay is { } replay)
        {
            if (replay < 0)
            {
                return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] replay must be a case index of zero or more"));
            }

            first = replay;
            cases = 1;
        }

        Result<KnownIssues> known = LoadKnownIssues();
        if (known.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(known.Error));
        }

        ulong seed = args.Seed ?? NewSeed();
        KnownIssues issues = known.Value!;
        IFuzzTransport transport = _transport;
        GatewayPacer pacer = _pacer;
        TimeProvider time = _time;
        FuzzTarget chosen = target.Value!;
        int firstIndex = first;
        int count = cases;
        LabToolDispatcher dispatcher = _services.GetRequiredService<LabToolDispatcher>();
        Func<string, bool> leakCheck = LeakCheck(_services.GetRequiredService<SecretVault>(), _services.GetRequiredService<EdgeState>());
        Result<JobHandle> started = _jobs.Start(FuzzJob.Kind, seed, (jobContext, token) =>
            new FuzzJob(transport, pacer, time, issues, [],
                (name, arguments, callContext, callToken) => dispatcher.InvokeAsync(name, arguments, callContext, callToken),
                leakCheck).RunAsync(seed, chosen, firstIndex, count, token));
        return Task.FromResult(started.IsFailure
            ? Result<object>.Failure(started.Error)
            : Result<object>.Success(new FuzzRunStarted(started.Value!.JobId, seed)));
    }

    /// <summary>
    /// Loads the shipped known-issue list under the repo root, validated against that root. An unset repo root means an empty
    /// list; a list that is present but unreadable or invalid refuses the run rather than letting every failure through.
    /// </summary>
    private Result<KnownIssues> LoadKnownIssues()
    {
        if (string.IsNullOrEmpty(_paths.RepoRoot))
        {
            return Result<KnownIssues>.Success(KnownIssues.Empty);
        }

        string expected = Path.Combine([_paths.RepoRoot, .. KnownIssuesPath]);
        Result<string> path = _validator.ValidatePath(expected, [_paths.RepoRoot]);
        if (path.IsFailure)
        {
            return Result<KnownIssues>.Failure(path.Error);
        }

        if (!File.Exists(path.Value!))
        {
            return Result<KnownIssues>.Failure("[Lab.NotFound] known-issues.toml is absent under the repo root");
        }

        return KnownIssues.Load(path.Value!);
    }

    private static ulong NewSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}
