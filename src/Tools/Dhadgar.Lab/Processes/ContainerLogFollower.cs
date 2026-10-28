using System.Globalization;
using Dhadgar.Lab.Daemon;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab.Processes;

/// <summary>
/// Captures a lab container's logs through a supervised <c>docker logs -f</c> child, not through the Docker API (L07).
/// The child is never relaunched: L09 and L31 call <see cref="FollowAsync"/> again after each container start.
/// </summary>
public sealed class ContainerLogFollower
{
    /// <summary>
    /// The configuration key that names the Linux Docker context.
    /// </summary>
    public const string LinuxContextKey = "Lab:Docker:LinuxContext";

    /// <summary>
    /// The Linux context used when the key is not set.
    /// </summary>
    public const string DefaultLinuxContext = "desktop-linux";

    private const int MaxContainerNameLength = 100;
    private const int MaxContextLength = 64;

    private readonly LabProcessSupervisor _supervisor;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly LabPaths _paths;

    /// <summary>
    /// Creates the follower over the supervisor and the lab configuration.
    /// </summary>
    public ContainerLogFollower(LabProcessSupervisor supervisor, IConfiguration configuration, TimeProvider time, LabPaths paths)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Starts (or restarts) the log child for a container. The child's process name is the service name.
    /// </summary>
    /// <param name="service">The lab service name, which is also the supervised process name.</param>
    /// <param name="container">The container name, which must start with <c>meridian-lab-</c>.</param>
    /// <param name="ct">Cancels the readiness wait; the follow itself has no readiness probe.</param>
    public async Task<Result> FollowAsync(string service, string container, CancellationToken ct)
    {
        Result<LabProcessSpec> spec = BuildSpec(service, container, _time.GetUtcNow());
        if (spec.IsFailure)
        {
            return Result.Failure(spec.Error);
        }

        // An earlier follower for the same service is replaced; a missing one is not an error.
        _ = await _supervisor.StopAsync(service).ConfigureAwait(false);
        Result<ProcessStatusDto> started = await _supervisor.StartAsync(spec.Value!, ct).ConfigureAwait(false);
        return started.IsSuccess ? Result.Success() : Result.Failure(started.Error);
    }

    /// <summary>
    /// Builds the <c>docker</c> spec. The arguments start with <c>--context</c>; <c>Relaunch</c> is false.
    /// </summary>
    internal Result<LabProcessSpec> BuildSpec(string service, string container, DateTimeOffset since)
    {
        if (string.IsNullOrEmpty(service) || !IsSafeName(service, MaxContainerNameLength))
        {
            return Result<LabProcessSpec>.Failure("[Lab.InvalidArgument] service name is not valid");
        }

        if (!container.StartsWith(LabDefaults.ContainerPrefix, StringComparison.Ordinal) || !IsSafeName(container, MaxContainerNameLength))
        {
            return Result<LabProcessSpec>.Failure("[Lab.BlastRadius] the container is not a lab container");
        }

        string context = _configuration[LinuxContextKey] ?? DefaultLinuxContext;
        if (!IsSafeName(context, MaxContextLength))
        {
            return Result<LabProcessSpec>.Failure("[Lab.InvalidArgument] the Docker context name is not valid");
        }

        string sinceText = since.ToString("O", CultureInfo.InvariantCulture);
        var arguments = new List<string> { "--context", context, "logs", "-f", "--since", sinceText, container };
        return Result<LabProcessSpec>.Success(new LabProcessSpec(
            service,
            "docker",
            arguments,
            new Dictionary<string, string>(StringComparer.Ordinal),
            _paths.Root,
            null,
            Relaunch: false));
    }

    private static bool IsSafeName(string value, int maxLength)
    {
        if (value.Length == 0 || value.Length > maxLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
