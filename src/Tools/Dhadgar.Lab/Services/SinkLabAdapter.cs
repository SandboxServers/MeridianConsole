using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// The webhook and email sink (L36) that the Notifications and Discord webhook proxies point at (<c>meridian-lab-sink:8080</c>).
/// It runs the probe image as <c>meridian-lab-sink</c>, has no config of its own, and is published on loopback port
/// <see cref="LabPorts.Sink"/> with a 128 MiB memory cap, because it records every inbound body.
/// </summary>
public sealed class SinkLabAdapter : ILabServiceAdapter
{
    /// <inheritdoc />
    public string Name => "sink";

    /// <inheritdoc />
    public string Container => "meridian-lab-sink";

    /// <inheritdoc />
    public string Project => "probe";

    /// <inheritdoc />
    public int HostPort => LabPorts.Sink;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = [];

    /// <inheritdoc />
    public string HealthPath => "/_sink/requests";

    /// <inheritdoc />
    public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
