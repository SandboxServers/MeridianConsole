using System.Text.RegularExpressions;
using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab.Pool;

/// <summary>
/// Linux member provider (L30): one <c>docker run</c> per member on the lab network, from the member image. The provider creates
/// the labelled lab network when it is missing (L37f1). Every call goes through <see cref="IDockerCli"/> with <c>ArgumentList</c>
/// (R-DOCKER). No volume and no published port (R-BLAST).
/// </summary>
public sealed class LinuxContainerProvider : IPoolProvider
{
    /// <summary>The container name prefix; the member id follows it.</summary>
    public const string ContainerPrefix = "meridian-lab-m-";

    private const string DefaultImage = LabBuilder.LinuxMemberLatestTag;
    private const string ImagePattern = @"^[A-Za-z0-9][A-Za-z0-9._/:@-]{0,255}\z";
    private const string MemberLabelFormat = "{{index .Config.Labels \"meridian-lab\"}} {{index .Config.Labels \"meridian-lab-member\"}}";

    private readonly IDockerCli _docker;
    private readonly string _image;

    /// <summary>
    /// Creates the provider. The image comes from <c>Lab:Pool:LinuxImage</c>; the default is the member image <c>lab_build</c> tags as latest.
    /// A value that is not a plain image reference falls back to the default.
    /// </summary>
    public LinuxContainerProvider(IDockerCli docker, IConfiguration configuration)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        ArgumentNullException.ThrowIfNull(configuration);
        string? configured = configuration["Lab:Pool:LinuxImage"];
        _image = configured is { Length: > 0 } && Regex.IsMatch(configured, ImagePattern) ? configured : DefaultImage;
    }

    /// <inheritdoc />
    public string Kind => "linux-container";

    /// <inheritdoc />
    public PoolDimensions Dimensions { get; } = new("linux", null, "debian", "none", "container", "x64", "host", false);

    /// <inheritdoc />
    public async Task<Result<bool>> IsAvailableAsync(CancellationToken ct)
    {
        Result<DockerOutput> inspect = await _docker.RunAsync(DockerEngine.Linux, ["image", "inspect", "--format", "{{.Id}}", "--", _image], ct)
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
        if (string.IsNullOrEmpty(member.MemberId))
        {
            return Result.Failure("[Lab.Docker] member has no container");
        }

        // The name is rebuilt from the member id, the value the guard verified, never taken from the stored name (N3).
        string name = ContainerPrefix + member.MemberId;
        Result<DockerOutput> removed = await _docker.RunAsync(DockerEngine.Linux, ["rm", "-f", name], ct).ConfigureAwait(false);
        if (removed.IsFailure)
        {
            return Result.Failure(removed.Error);
        }

        // Exit 1 with "No such container" means the container is already gone, which is the state a destroy wants.
        return removed.Value.ExitCode == 0 || removed.Value.StdErr.Contains("No such container", StringComparison.Ordinal)
            ? Result.Success()
            : Result.Failure("[Lab.Docker] member container could not be removed");
    }

    /// <summary>
    /// Makes sure the labelled lab network exists before a member is run on it (L37f1). Creates it only when inspect says it is missing.
    /// </summary>
    private async Task<Result> EnsureNetworkAsync(CancellationToken ct)
    {
        Result<DockerOutput> inspect = await _docker.RunAsync(DockerEngine.Linux, ["network", "inspect", LabDefaults.NetworkName], ct).ConfigureAwait(false);
        if (inspect.IsFailure)
        {
            return Result.Failure(inspect.Error);
        }

        if (inspect.Value.ExitCode == 0)
        {
            return Result.Success();
        }

        Result<DockerOutput> created = await _docker.RunAsync(DockerEngine.Linux, ["network", "create", "--label", LabDefaults.ContainerLabel + "=1", LabDefaults.NetworkName], ct).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return Result.Failure(created.Error);
        }

        if (created.Value.ExitCode == 0)
        {
            return Result.Success();
        }

        // The stack or another provision may have created it first (D-ML172).
        Result<DockerOutput> again = await _docker.RunAsync(DockerEngine.Linux, ["network", "inspect", LabDefaults.NetworkName], ct).ConfigureAwait(false);
        return again.IsSuccess && again.Value.ExitCode == 0
            ? Result.Success()
            : Result.Failure("[Lab.Docker] could not create the lab network");
    }

    private async Task<Result<PoolMember>> RunMemberAsync(string memberId, CancellationToken ct)
    {
        string name = ContainerPrefix + memberId;
        Result network = await EnsureNetworkAsync(ct).ConfigureAwait(false);
        if (network.IsFailure)
        {
            return Result<PoolMember>.Failure(network.Error);
        }

        Result<DockerOutput> run;
        try
        {
            run = await _docker.RunAsync(
                DockerEngine.Linux,
                [
                    "run", "-d",
                    "--name", name,
                    "--label", "meridian-lab=1",
                    "--label", "meridian-lab-member=" + memberId,
                    "--network", LabDefaults.NetworkName,
                    "--restart", "no",
                    "--memory", "1g",
                    "--",
                    _image,
                ],
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The run may have created the container before the cancel landed; remove it, then rethrow (N4).
            await RemoveOwnContainerAsync(memberId, name, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (run.IsFailure || run.Value.ExitCode != 0)
        {
            // A failed run can still leave the named container behind; remove it only when it carries this member's labels (N4).
            await RemoveOwnContainerAsync(memberId, name, ct).ConfigureAwait(false);
            return Result<PoolMember>.Failure(run.IsFailure ? run.Error : "[Lab.Docker] member container could not be started");
        }

        return Result<PoolMember>.Success(new PoolMember(
            memberId,
            "m:" + memberId,
            name,
            Dimensions,
            MemberState.Ready,
            DateTimeOffset.MinValue));
    }

    /// <summary>
    /// Removes the named container only when it carries both lab labels with this member id. A name conflict with a foreign
    /// container, or a container that does not exist, is left alone. Best effort: the outcome is not reported.
    /// </summary>
    private async Task RemoveOwnContainerAsync(string memberId, string name, CancellationToken ct)
    {
        Result<DockerOutput> labels = await _docker.RunAsync(
            DockerEngine.Linux,
            ["inspect", "--format", MemberLabelFormat, "--", name],
            ct).ConfigureAwait(false);
        if (labels.IsFailure || labels.Value.ExitCode != 0 || !string.Equals(labels.Value.StdOut.Trim(), "1 " + memberId, StringComparison.Ordinal))
        {
            return;
        }

        _ = await _docker.RunAsync(DockerEngine.Linux, ["rm", "-f", name], ct).ConfigureAwait(false);
    }
}
