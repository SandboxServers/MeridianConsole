using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// Linux member provider (L30): one <c>docker run</c> per member on the lab network, from the member image. Every call goes
/// through <see cref="IDockerCli"/> with <c>ArgumentList</c> (R-DOCKER). No volume and no published port (R-BLAST).
/// </summary>
public sealed class LinuxContainerProvider : IPoolProvider
{
    /// <summary>The container name prefix; the member id follows it.</summary>
    public const string ContainerPrefix = "meridian-lab-m-";

    private const string DefaultImage = "meridian-lab/linux-member:latest";
    private const string LabNetwork = "meridian-lab-net";

    private readonly IDockerCli _docker;
    private readonly string _image;

    /// <summary>
    /// Creates the provider. The image comes from <c>Lab:Pool:LinuxImage</c>; the default names the untagged member image.
    /// </summary>
    public LinuxContainerProvider(IDockerCli docker, IConfiguration configuration)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        ArgumentNullException.ThrowIfNull(configuration);
        _image = configuration["Lab:Pool:LinuxImage"] is { Length: > 0 } image ? image : DefaultImage;
    }

    /// <inheritdoc />
    public string Kind => "linux-container";

    /// <inheritdoc />
    public PoolDimensions Dimensions { get; } = new("linux", null, "debian", "none", "container", "x64", "host", false);

    /// <inheritdoc />
    public async Task<Result<bool>> IsAvailableAsync(CancellationToken ct)
    {
        Result<DockerOutput> inspect = await _docker.RunAsync(DockerEngine.Linux, ["image", "inspect", "--format", "{{.Id}}", _image], ct)
            .ConfigureAwait(false);
        if (inspect.IsFailure)
        {
            return Result<bool>.Failure(inspect.Error);
        }

        return inspect.Value.ExitCode == 0
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("[Lab.Unavailable] linux member image is not built; run lab_build");
    }

    /// <inheritdoc />
    public Task<Result<PoolMember>> ProvisionAsync(PoolConstraints constraints, string memberId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        return RunMemberAsync(memberId, ct);
    }

    /// <inheritdoc />
    public async Task<Result<PoolMember>> ResetAsync(PoolMember member, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(member);
        Result destroyed = await DestroyAsync(member, ct).ConfigureAwait(false);
        if (destroyed.IsFailure)
        {
            return Result<PoolMember>.Failure(destroyed.Error);
        }

        return await RunMemberAsync(member.MemberId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DestroyAsync(PoolMember member, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (string.IsNullOrEmpty(member.ContainerName))
        {
            return Result.Failure("[Lab.Docker] member has no container");
        }

        Result<DockerOutput> removed = await _docker.RunAsync(DockerEngine.Linux, ["rm", "-f", member.ContainerName], ct).ConfigureAwait(false);
        if (removed.IsFailure)
        {
            return Result.Failure(removed.Error);
        }

        // Exit 1 with "No such container" means the container is already gone, which is the state a destroy wants.
        return removed.Value.ExitCode == 0 || removed.Value.StdErr.Contains("No such container", StringComparison.Ordinal)
            ? Result.Success()
            : Result.Failure("[Lab.Docker] member container could not be removed");
    }

    private async Task<Result<PoolMember>> RunMemberAsync(string memberId, CancellationToken ct)
    {
        string name = ContainerPrefix + memberId;
        Result<DockerOutput> run = await _docker.RunAsync(
            DockerEngine.Linux,
            [
                "run", "-d",
                "--name", name,
                "--label", "meridian-lab=1",
                "--label", "meridian-lab-member=" + memberId,
                "--network", LabNetwork,
                "--restart", "no",
                "--memory", "1g",
                _image,
            ],
            ct).ConfigureAwait(false);

        if (run.IsFailure)
        {
            return Result<PoolMember>.Failure(run.Error);
        }

        if (run.Value.ExitCode != 0)
        {
            return Result<PoolMember>.Failure("[Lab.Docker] member container could not be started");
        }

        return Result<PoolMember>.Success(new PoolMember(
            memberId,
            "m:" + memberId,
            name,
            Dimensions,
            MemberState.Ready,
            DateTimeOffset.MinValue));
    }
}
