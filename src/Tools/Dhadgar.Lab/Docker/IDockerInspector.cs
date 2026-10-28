using System.Globalization;
using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Docker;

/// <summary>
/// One mount of an inspected container.
/// </summary>
/// <param name="Type">The mount type: <c>volume</c>, <c>bind</c> and so on.</param>
/// <param name="Name">The volume name, or null for a bind mount.</param>
/// <param name="Destination">The path inside the container.</param>
public sealed record MountInfo(string Type, string? Name, string Destination);

/// <summary>
/// The live state of one container, as <c>docker inspect</c> reports it now.
/// </summary>
/// <param name="Id">The full container id.</param>
/// <param name="Name">The container name with its leading <c>/</c>.</param>
/// <param name="Labels">The container labels.</param>
/// <param name="Mounts">The container mounts.</param>
/// <param name="RestartCount">How many times Docker restarted the container.</param>
/// <param name="StartedAt">When the container last started.</param>
/// <param name="Context">The Docker context the inspect ran in.</param>
public sealed record ContainerInspection(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<MountInfo> Mounts,
    int RestartCount,
    DateTimeOffset StartedAt,
    string Context);

/// <summary>
/// Live container inspection for the blast-radius guard (L19). Reads the container now; never trusts the registry alone.
/// </summary>
public interface IDockerInspector
{
    /// <summary>
    /// The Docker context the lab runs in. Every inspection runs in this context.
    /// </summary>
    string LabContext { get; }

    /// <summary>
    /// Inspects one container by id or name. Fails with <c>[Lab.BlastRadius]</c> when Docker cannot inspect it.
    /// </summary>
    Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct);
}

/// <summary>
/// The <c>docker inspect</c> implementation. The container reference is checked before it reaches the CLI.
/// </summary>
public sealed class DockerInspector(IDockerCli docker, DockerOptions options) : IDockerInspector
{
    // \z, not $: $ accepts a trailing newline (security N4).
    private static readonly System.Text.RegularExpressions.Regex ContainerRef =
        new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <inheritdoc />
    public string LabContext { get; } = options.LinuxContext;

    /// <summary>
    /// True when the value is a valid container reference (letters, digits, <c>_.-</c>, at most 128 characters).
    /// </summary>
    internal static bool IsValidContainerRef(string value)
    {
        try
        {
            return ContainerRef.IsMatch(value);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(containerId);
        if (!IsValidContainerRef(containerId))
        {
            return Result<ContainerInspection>.Failure("[Lab.InvalidArgument] container reference is not valid");
        }

        var run = await docker.RunAsync(DockerEngine.Linux, ["inspect", "--format", "{{json .}}", containerId], ct).ConfigureAwait(false);
        if (run.IsFailure || run.Value.ExitCode != 0)
        {
            return Result<ContainerInspection>.Failure("[Lab.BlastRadius] container could not be inspected");
        }

        try
        {
            using var document = JsonDocument.Parse(run.Value.StdOut);
            var root = document.RootElement;
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("Config", out var config) && config.TryGetProperty("Labels", out var labelElement) && labelElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var label in labelElement.EnumerateObject())
                {
                    labels[label.Name] = label.Value.GetString() ?? string.Empty;
                }
            }

            var mounts = new List<MountInfo>();
            if (root.TryGetProperty("Mounts", out var mountElement) && mountElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var mount in mountElement.EnumerateArray())
                {
                    mounts.Add(new MountInfo(
                        mount.GetProperty("Type").GetString() ?? string.Empty,
                        mount.TryGetProperty("Name", out var name) ? name.GetString() : null,
                        mount.GetProperty("Destination").GetString() ?? string.Empty));
                }
            }

            var startedText = root.GetProperty("State").GetProperty("StartedAt").GetString() ?? string.Empty;
            if (!DateTimeOffset.TryParse(startedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startedAt))
            {
                return Result<ContainerInspection>.Failure("[Lab.BlastRadius] container start time is not readable");
            }

            return Result<ContainerInspection>.Success(new ContainerInspection(
                root.GetProperty("Id").GetString() ?? string.Empty,
                root.GetProperty("Name").GetString() ?? string.Empty,
                labels,
                mounts,
                root.TryGetProperty("RestartCount", out var restarts) ? restarts.GetInt32() : 0,
                startedAt,
                LabContext));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Result<ContainerInspection>.Failure("[Lab.BlastRadius] container inspect output is not readable");
        }
    }
}
