namespace Dhadgar.Lab.Invariants;

/// <summary>
/// The Gateway's YARP routes as (path template, cluster), copied from <c>src/Dhadgar.Gateway/appsettings.json</c>
/// <c>ReverseProxy:Routes</c>, in Gateway match order (<c>Order</c>, then config order). A <c>{name}</c> segment matches one
/// path segment; a <c>{**name}</c> segment matches the rest of the path. Other segments match literally, ignoring case.
/// <see cref="GatewayRouteTableTests"/> fails when a route is added to the Gateway and not here.
/// </summary>
public static class GatewayRouteTable
{
    /// <summary>
    /// The route table in match order.
    /// </summary>
    public static IReadOnlyList<(string Template, string ClusterId)> Routes { get; } =
    [
        ("/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**catch-all}", "console"),
        ("/api/v1/identity/internal/{**catch-all}", "identity"),
        ("/api/v1/organizations/{organizationId}/nodes/{nodeId}/reservations/{**catch-all}", "nodes"),
        ("/api/v1/betterauth/{**catch-all}", "betterauth"),
        ("/api/v1/identity/{**catch-all}", "identity"),
        ("/api/v1/organizations/{organizationId}/servers/{**catch-all}", "servers"),
        ("/api/v1/organizations/{organizationId}/nodes/{**catch-all}", "nodes"),
        ("/api/v1/organizations/{organizationId}/enrollment/{**catch-all}", "nodes"),
        ("/hubs/console/{**catch-all}", "console"),
        ("/hubs/agent/{**catch-all}", "nodes"),
        ("/api/v1/notifications/{**catch-all}", "notifications"),
        ("/api/v1/secrets/{**catch-all}", "secrets"),
        ("/api/v1/discord/{**catch-all}", "discord"),
        ("/api/v1/agents/ca-certificate", "nodes"),
        ("/api/v1/agents/enroll", "nodes"),
        ("/api/v1/agents/{**catch-all}", "nodes"),
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
        foreach ((string template, string cluster) in Routes)
        {
            if (Matches(template, path))
            {
                return cluster;
            }
        }

        return null;
    }

    private static bool Matches(string template, string path)
    {
        string[] parts = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.StartsWith("{**", StringComparison.Ordinal))
            {
                return true;
            }

            if (i >= segments.Length)
            {
                return false;
            }

            if (part.StartsWith('{'))
            {
                continue;
            }

            if (!string.Equals(part, segments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return parts.Length == segments.Length;
    }
}
