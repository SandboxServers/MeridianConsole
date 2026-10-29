using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Mutants;
using Dhadgar.Lab.Random;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tools.Mutants;

/// <summary>
/// Arguments of <c>mutation_run</c>. <c>Mutants</c> names product mutants under <c>tools/lab/mutants</c>; when it is empty every
/// product mutant runs. <c>LeaseId</c> is accepted for the envelope and not used: the job takes its own lease.
/// </summary>
public sealed record MutationRunArgs(IReadOnlyList<string>? Mutants = null, string? LeaseId = null);

/// <summary>The <c>mutation_run</c> payload: the job that runs the mutants.</summary>
/// <param name="JobId">The job id; follow it with <c>job_status</c>. The job writes <c>mutants.json</c> and <c>mutants.md</c>.</param>
public sealed record MutationRunStarted(string JobId);

/// <summary>
/// <c>mutation_run</c> (OwnLease): starts a <see cref="MutationRunJob"/> over the named product mutants and returns its job id.
/// Unknown names are refused before the job starts. Only product mutants run; lab mutants belong to the self-check (L55).
/// </summary>
public sealed class MutationRunTool : LabTool<MutationRunArgs>
{
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
    public MutationRunTool(JobRunner jobs, GatewayPacer pacer, LabPaths paths, IPathValidator validator, TimeProvider time, IServiceProvider services)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "mutation_run";

    /// <inheritdoc />
    public override string Description => "Starts a mutation job over product mutants (default all): each is built from HEAD with its patch, its services are started on the mutant build, its detector runs, and the plain build is restored. Returns job_id; the job writes mutants.json and mutants.md with a verdict per mutant (detected, detected_other, survived, stale or unreachable).";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(MutationRunArgs args, LabCallContext context, CancellationToken ct)
    {
        Result<IReadOnlyList<MutantSpec>> loaded = LoadProductMutants();
        if (loaded.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(loaded.Error));
        }

        var selected = new List<MutantSpec>();
        if (args.Mutants is null || args.Mutants.Count == 0)
        {
            selected.AddRange(loaded.Value!);
        }
        else
        {
            foreach (string id in args.Mutants)
            {
                MutantSpec? spec = loaded.Value!.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
                if (spec is null)
                {
                    return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] unknown mutant"));
                }

                selected.Add(spec);
            }
        }

        if (selected.Count == 0)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.NotFound] no product mutants to run"));
        }

        var job = new MutationRunJob(
            selected,
            BuildOneAsync,
            PlainBuildId,
            (name, arguments, callContext, callToken) => _services.GetRequiredService<LabToolDispatcher>().InvokeAsync(name, arguments, callContext, callToken),
            _pacer,
            _validator,
            _time,
            _services.GetRequiredService<Redactor>());

        Result<JobHandle> started = _jobs.Start(MutationRunJob.Kind, LabRandom.NewSeed(), (jobContext, token) =>
            job.RunAsync(jobContext.JobId, jobContext.LeaseId, jobContext.Folder, token));
        return Task.FromResult(started.IsFailure
            ? Result<object>.Failure(started.Error)
            : Result<object>.Success(new MutationRunStarted(started.Value!.JobId)));
    }

    /// <summary>Loads every mutant folder under <c>tools/lab/mutants</c> and keeps the product ones.</summary>
    private Result<IReadOnlyList<MutantSpec>> LoadProductMutants()
    {
        if (string.IsNullOrWhiteSpace(_paths.RepoRoot))
        {
            return Result<IReadOnlyList<MutantSpec>>.Failure("[Lab.InvalidArgument] the repo root is not configured");
        }

        Result<string> mutantsRoot = _validator.ValidatePath(Path.Combine(_paths.RepoRoot, "tools", "lab", "mutants"), [_paths.RepoRoot]);
        if (mutantsRoot.IsFailure)
        {
            return Result<IReadOnlyList<MutantSpec>>.Failure("[Lab.InvalidArgument] mutants folder is not valid");
        }

        Result<IReadOnlyList<MutantSpec>> specs = MutantSpecLoader.LoadAll(mutantsRoot.Value!, _paths.RepoRoot, _validator);
        if (specs.IsFailure)
        {
            return specs;
        }

        return Result<IReadOnlyList<MutantSpec>>.Success(specs.Value!.Where(spec => spec.Kind == MutantKind.Product).ToList());
    }

    /// <summary>Builds one mutant with the production runner, publishing the projects its <c>services</c> name.</summary>
    private async Task<Result<MutantBuildDto>> BuildOneAsync(MutantSpec spec, CancellationToken ct)
    {
        var projects = new List<BuildProject>();
        foreach (string name in spec.Services)
        {
            Result<BuildProject> project = LabBuilder.ResolveProject(name);
            if (project.IsFailure)
            {
                return Result<MutantBuildDto>.Failure(project.Error);
            }

            projects.Add(project.Value!);
        }

        return await MutantBuilder.BuildAsync(
            spec, projects, _paths, _services.GetRequiredService<IDockerCli>(), _validator, _time, LabBuilder.RunAsync, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The latest plain build the lab made, or null when there is none.</summary>
    private string? PlainBuildId()
    {
        BuildStamp stamp = LastBuildReader.Read(_paths, _validator);
        return stamp.Id == BuildStamp.None.Id ? null : stamp.Id;
    }
}
