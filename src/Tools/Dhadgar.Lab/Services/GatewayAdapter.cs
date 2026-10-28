using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// Gateway behind the lab proxies (C1 port 15000). Reaches Nodes only through <c>gateway-nodes</c> and Redis only through
/// <c>gateway-redis</c>, and never calls the dynamic Cloudflare fetch.
/// </summary>
public sealed class GatewayAdapter : ILabServiceAdapter
{
    /// <summary>Toxiproxy container name on the lab network.</summary>
    public const string ToxiproxyHost = "meridian-lab-toxiproxy";

    /// <summary>Gateway to Nodes proxy port (C10 <c>gateway-nodes</c>).</summary>
    public const int GatewayNodesProxyPort = 20012;

    /// <summary>Gateway to Redis proxy port (C10 <c>gateway-redis</c>).</summary>
    public const int GatewayRedisProxyPort = 20010;

    /// <inheritdoc />
    public string Name => "gateway";

    /// <inheritdoc />
    public string Container => "meridian-lab-gateway";

    /// <inheritdoc />
    public string Project => "gateway";

    /// <inheritdoc />
    public int HostPort => 15000;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = ["gateway-redis", "gateway-nodes"];

    /// <inheritdoc />
    public string HealthPath => "/healthz";

    /// <inheritdoc />
    public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Logging__LogLevel__Default",
    };

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Logging__Console__FormatterName"] = "json",
            ["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["ReverseProxy__Clusters__nodes__Destinations__d1__Address"] = $"http://{ToxiproxyHost}:{GatewayNodesProxyPort}/",
            ["Redis__ConnectionString"] = $"{ToxiproxyHost}:{GatewayRedisProxyPort}",
            ["Cloudflare__EnableDynamicFetch"] = "false",
            ["Readyz__RequiredClusters__0"] = "nodes",
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }
}
