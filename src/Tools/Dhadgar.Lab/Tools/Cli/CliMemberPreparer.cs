using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Pool;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Cli;

/// <summary>
/// Installs the lab CLI into a pool member after the pool provisions or resets it (D-ML173). It delegates to
/// <see cref="CliMemberInstall.InstallIntoMemberAsync"/>, the same install that <c>pool_acquire</c> and <c>member_reset</c> use.
/// </summary>
public sealed class CliMemberPreparer : IPoolMemberPreparer
{
    private readonly IDockerCli _docker;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the preparer.
    /// </summary>
    /// <param name="docker">The Docker CLI wrapper that copies the binaries in.</param>
    /// <param name="paths">The lab paths that hold the CLI build output.</param>
    /// <param name="validator">The path validator for every path the install reads.</param>
    public CliMemberPreparer(IDockerCli docker, LabPaths paths, IPathValidator validator)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc />
    public Task<Result> PrepareAsync(PoolMember member, CancellationToken ct)
    {
        return CliMemberInstall.InstallIntoMemberAsync(_docker, _paths, _validator, member, ct);
    }
}
