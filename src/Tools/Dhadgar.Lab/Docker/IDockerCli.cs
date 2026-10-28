using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Docker;

/// <summary>
/// Which docker engine a call targets. The context name comes from configuration (C10), never from a caller.
/// </summary>
public enum DockerEngine
{
    /// <summary>The Linux engine (Docker Desktop, <c>Lab:Docker:LinuxContext</c>).</summary>
    Linux,

    /// <summary>The Windows engine (<c>Lab:Docker:WindowsContext</c>, set from the L00 worknote).</summary>
    Windows,
}

/// <summary>
/// One completed docker run. A non-zero exit code is returned here; callers decide what it means.
/// </summary>
public sealed record DockerOutput(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// The only way the lab talks to docker (R-DOCKER). Every call carries <c>--context &lt;name&gt;</c> first.
/// </summary>
public interface IDockerCli
{
    /// <summary>
    /// Runs <c>docker --context &lt;name&gt; args...</c> through <c>ArgumentList</c>. Fails with <c>[Lab.Docker]</c> when
    /// docker cannot start, the context is not configured, or the run times out. Arguments are never echoed in errors.
    /// </summary>
    Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct);
}
