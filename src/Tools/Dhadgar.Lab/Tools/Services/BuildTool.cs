using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Services;

/// <summary>
/// Arguments of <c>lab_build</c>. <c>checkout</c> is the repo root or a worktree under it; <c>projects</c> defaults to every
/// L09 project. <c>baseline</c> and <c>mutant</c> arrive in later packets.
/// </summary>
public sealed record LabBuildArgs(string? Checkout = null, IReadOnlyList<string>? Projects = null);

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
    public override string Description => "Publish gateway, nodes and probe from a checkout (locked restore, no runtime identifier) and build their lab images.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(LabBuildArgs args, LabCallContext context, CancellationToken ct)
    {
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
}
