using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Mutants;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Services;

/// <summary>
/// Arguments of <c>lab_build</c>. <c>checkout</c> is the repo root or a worktree under it; <c>projects</c> defaults to every
/// L09 project. <c>mutant</c> (L54) names a product mutant: the build comes from <c>git archive HEAD</c> of the repo root,
/// <c>checkout</c> is ignored and <c>projects</c> defaults to the mutant's <c>services</c>. <c>baseline</c> arrives in L60.
/// </summary>
public sealed record LabBuildArgs(string? Checkout = null, IReadOnlyList<string>? Projects = null, string? Mutant = null);

/// <summary>
/// <c>lab_build</c> (Leased): publishes the projects from a checkout, builds their lab images and records the build.
/// </summary>
public sealed class BuildTool : LabTool<LabBuildArgs>
{
    private readonly LabPaths _paths;
    private readonly IDockerCli _docker;
    private readonly IPathValidator _validator;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the tool over the lab layout, the docker CLI, the path validator and the clock.
    /// </summary>
    public BuildTool(LabPaths paths, IDockerCli docker, IPathValidator validator, TimeProvider time)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public override string Name => "lab_build";

    /// <inheritdoc />
    public override string Description => "Publish gateway, nodes, notifications, discord and probe from a checkout (locked restore, no runtime identifier) and build their lab images.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(LabBuildArgs args, LabCallContext context, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(args.Mutant))
        {
            return await BuildMutantAsync(args.Mutant, args.Projects, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(args.Checkout))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] checkout is required");
        }

        var selected = new List<BuildProject>();
        IEnumerable<string> names = args.Projects ?? LabBuilder.Projects.Select(project => project.Name);
        foreach (string name in names)
        {
            Result<BuildProject> project = LabBuilder.ResolveProject(name);
            if (project.IsFailure)
            {
                return Result<object>.Failure(project.Error);
            }

            selected.Add(project.Value!);
        }

        if (selected.Count == 0)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] at least one project is required");
        }

        Result<LabBuildDto> built = await LabBuilder.BuildAsync(args.Checkout, selected, _docker, _paths, _validator, _time, ct)
            .ConfigureAwait(false);
        return built.IsFailure ? Result<object>.Failure(built.Error) : Result<object>.Success(built.Value!);
    }

    /// <summary>
    /// Builds one product mutant (L54, A3) from <c>git archive HEAD</c>. The spec comes from <c>tools\lab\mutants</c> under the
    /// repo root; an unknown id is <c>[Lab.InvalidArgument]</c>. A patch that no longer applies returns the stale verdict.
    /// </summary>
    private async Task<Result<object>> BuildMutantAsync(string mutantId, IReadOnlyList<string>? projects, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_paths.RepoRoot))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] the repo root is not configured");
        }

        Result<string> mutantsRoot = _validator.ValidatePath(Path.Combine(_paths.RepoRoot, "tools", "lab", "mutants"), [_paths.RepoRoot]);
        if (mutantsRoot.IsFailure)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] mutants folder is not valid");
        }

        Result<IReadOnlyList<MutantSpec>> specs = MutantSpecLoader.LoadAll(mutantsRoot.Value!, _paths.RepoRoot, _validator);
        if (specs.IsFailure)
        {
            return Result<object>.Failure(specs.Error);
        }

        MutantSpec? spec = specs.Value!.FirstOrDefault(candidate => string.Equals(candidate.Id, mutantId, StringComparison.Ordinal));
        if (spec is null)
        {
            return Result<object>.Failure("[Lab.InvalidArgument] unknown mutant");
        }

        var selected = new List<BuildProject>();
        IEnumerable<string> names = projects ?? spec.Services;
        foreach (string name in names)
        {
            Result<BuildProject> project = LabBuilder.ResolveProject(name);
            if (project.IsFailure)
            {
                return Result<object>.Failure(project.Error);
            }

            selected.Add(project.Value!);
        }

        Result<MutantBuildDto> built = await MutantBuilder.BuildAsync(spec, selected, _paths, _docker, _validator, _time, LabBuilder.RunAsync, ct)
            .ConfigureAwait(false);
        return built.IsFailure ? Result<object>.Failure(built.Error) : Result<object>.Success(built.Value!);
    }
}
