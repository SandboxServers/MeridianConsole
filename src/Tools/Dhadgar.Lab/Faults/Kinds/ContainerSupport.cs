using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// Heal data of a container fault: the container the checks passed on.
/// </summary>
public sealed record ContainerHealData(string ContainerId);

/// <summary>
/// Shared helpers for the container fault kinds (kill, pause, disk fill). Every docker call goes through <see cref="IDockerCli"/>
/// with separate argument items (R-ARGS); the caller decides what a non-zero exit code means.
/// </summary>
internal static class ContainerSupport
{
    /// <summary>
    /// Runs one docker command on the Linux engine. Only a docker that cannot start is a failure here; a non-zero exit is returned.
    /// </summary>
    public static Task<Result<DockerOutput>> RunAsync(IDockerCli docker, CancellationToken ct, params string[] args) =>
        docker.RunAsync(DockerEngine.Linux, args, ct);

    /// <summary>
    /// The container id from the resolved target, or a refusal when the target has none.
    /// </summary>
    public static Result<string> ContainerIdOf(ResolvedTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return string.IsNullOrEmpty(target.ContainerId)
            ? Result<string>.Failure("[Lab.BlastRadius] target has no registered container")
            : Result<string>.Success(target.ContainerId);
    }

    /// <summary>
    /// True when the docker output says the thing is already in the wanted state or already gone.
    /// </summary>
    public static bool SaysAlreadyDone(DockerOutput output, params string[] phrases) =>
        phrases.Any(p => output.StdErr.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>The label inspect format (the lab label must read "1").</summary>
    internal const string LabelFormat = "{{index .Config.Labels \"meridian-lab\"}}";

    /// <summary>
    /// Security N4: before a heal mutates a container named by the journal, the reference must be valid and the container must
    /// carry the lab label. A container that no longer exists is healed (nothing to undo).
    /// </summary>
    public static async Task<Result> CheckHealRefAsync(IDockerCli docker, string containerId, CancellationToken ct)
    {
        if (!DockerInspector.IsValidContainerRef(containerId))
        {
            return Result.Failure("[Lab.BlastRadius] journal container reference is not valid");
        }

        Result<DockerOutput> run = await RunAsync(docker, ct, "inspect", "--format", LabelFormat, containerId).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result.Failure(run.Error);
        }

        if (run.Value.ExitCode != 0)
        {
            return SaysAlreadyDone(run.Value, "No such")
                ? Result.Success()
                : Result.Failure("[Lab.Docker] container could not be inspected");
        }

        return string.Equals(run.Value.StdOut.Trim(), "1", StringComparison.Ordinal)
            ? Result.Success()
            : Result.Failure("[Lab.BlastRadius] journal container is not labelled as lab-owned");
    }
}
