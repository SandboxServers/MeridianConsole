using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// The Panel behind the lab (C1 port 14321). It has no proxied dependencies: it talks to BetterAuth and the Gateway
/// from the browser through the Gateway URL that is built into its image.
/// </summary>
public sealed class PanelLabAdapter : ILabServiceAdapter
{
    /// <inheritdoc />
    public string Name => "panel";

    /// <inheritdoc />
    public string Container => "meridian-lab-panel";

    /// <inheritdoc />
    public string Project => "panel";

    /// <inheritdoc />
    public int HostPort => 14321;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = [];

    /// <inheritdoc />
    public string HealthPath => "/login";

    /// <inheritdoc />
    public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PORT"] = "8080",
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }
}
