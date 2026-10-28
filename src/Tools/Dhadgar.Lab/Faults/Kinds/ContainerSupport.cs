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
}
