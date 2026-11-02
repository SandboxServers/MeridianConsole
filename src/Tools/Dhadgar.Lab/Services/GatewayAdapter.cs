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

    /// <summary>Gateway to Identity proxy port (C10 <c>gateway-identity</c>).</summary>
    public const int GatewayIdentityProxyPort = 20011;

    /// <summary>Gateway to BetterAuth proxy port (C10 <c>gateway-betterauth</c>).</summary>
    public const int GatewayBetterAuthProxyPort = 20013;

    /// <summary>Gateway to Notifications proxy port (C10 <c>gateway-notifications</c>, L36).</summary>
    public const int GatewayNotificationsProxyPort = 20014;

    /// <summary>Gateway to Discord proxy port (C10 <c>gateway-discord</c>, L36).</summary>
    public const int GatewayDiscordProxyPort = 20015;

    /// <inheritdoc />
    public string Name => "gateway";

    /// <inheritdoc />
    public string Container => "meridian-lab-gateway";

    /// <inheritdoc />
    public string Project => "gateway";

    /// <inheritdoc />
    public int HostPort => 15000;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = ["gateway-redis", "gateway-nodes", "gateway-identity", "gateway-betterauth", "gateway-notifications", "gateway-discord"];

    /// <inheritdoc />
    public string HealthPath => "/healthz";

    /// <inheritdoc />
    public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Logging:LogLevel:Default",
        "Logging:LogLevel:Dhadgar",
        "Logging:LogLevel:Microsoft.AspNetCore",
        "Readyz:RequiredClusters:0",
        "Readyz:RequiredClusters:1",
        "Readyz:RequiredClusters:2",
        "Readyz:RequiredClusters:3",
    };

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        // The vault-built string (with the Redis password) wins; the bare proxy address is the fallback.
        string redis = ctx.ConnectionStrings.TryGetValue("gateway-redis", out string? vaultRedis) && !string.IsNullOrWhiteSpace(vaultRedis)
            ? vaultRedis
            : $"{ToxiproxyHost}:{GatewayRedisProxyPort}";
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Logging__Console__FormatterName"] = "json",
            ["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["ReverseProxy__Clusters__nodes__Destinations__d1__Address"] = $"http://{ToxiproxyHost}:{GatewayNodesProxyPort}/",
            ["ReverseProxy__Clusters__identity__Destinations__d1__Address"] = $"http://{ToxiproxyHost}:{GatewayIdentityProxyPort}/",
            ["ReverseProxy__Clusters__betterauth__Destinations__d1__Address"] = $"http://{ToxiproxyHost}:{GatewayBetterAuthProxyPort}/",
            ["ReverseProxy__Clusters__notifications__Destinations__d1__Address"] = $"http://{ToxiproxyHost}:{GatewayNotificationsProxyPort}/",
            ["ReverseProxy__Clusters__discord__Destinations__d1__Address"] = $"http://{ToxiproxyHost}:{GatewayDiscordProxyPort}/",
            ["Redis__ConnectionString"] = redis,
            ["Cloudflare__EnableDynamicFetch"] = "false",
            ["Readyz__RequiredClusters__0"] = "nodes",
            ["Readyz__RequiredClusters__1"] = "identity",
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }
}
