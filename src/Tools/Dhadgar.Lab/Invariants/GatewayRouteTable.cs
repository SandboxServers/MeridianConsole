namespace Dhadgar.Lab.Invariants;

/// <summary>
/// The Gateway's YARP routes as (path prefix, cluster), copied from <c>src/Dhadgar.Gateway/appsettings.json</c>
/// <c>ReverseProxy:Routes</c>, most specific first. An entry ending in <c>/</c> matches by prefix; any other entry matches exactly.
/// <see cref="GatewayRouteTableTests"/> fails when a route is added to the Gateway and not here.
/// </summary>
public static class GatewayRouteTable
{
    /// <summary>
    /// The route table in match order.
    /// </summary>
    public static IReadOnlyList<(string PathPrefix, string ClusterId)> Routes { get; } =
    [
        ("/api/v1/identity/internal/", "identity"),
        ("/api/v1/betterauth/", "betterauth"),
        ("/api/v1/identity/", "identity"),
        ("/api/v1/servers/", "servers"),
        ("/api/v1/organizations/", "nodes"),          // nodes-org-route and enrollment-route
        ("/api/v1/console/", "console"),
        ("/hubs/console/", "console"),
        ("/hubs/agent/", "nodes"),
        ("/api/v1/notifications/", "notifications"),
        ("/api/v1/secrets/", "secrets"),
        ("/api/v1/discord/", "discord"),
        ("/api/v1/agents/enroll", "nodes"),           // agents-enroll-route, exact
        ("/api/v1/agents/", "nodes"),
    ];

    /// <summary>
    /// The cluster a request path routes to, or null when no route matches (the Gateway answers 404 for it).
    /// </summary>
    public static string? ClusterFor(string? requestPath)
    {
        if (string.IsNullOrEmpty(requestPath))
        {
            return null;
        }

        int query = requestPath.IndexOf('?', StringComparison.Ordinal);
        string path = query >= 0 ? requestPath[..query] : requestPath;
        foreach ((string prefix, string cluster) in Routes)
        {
            bool matches = prefix.EndsWith('/')
                ? path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                : string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                return cluster;
            }
        }

        return null;
    }
}
