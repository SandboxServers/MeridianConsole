namespace Dhadgar.Lab.Invariants;

/// <summary>
/// One Gateway route: its YARP route id, its <c>Match:Path</c> template, its cluster and its <c>Order</c>.
/// </summary>
public sealed record GatewayRoute(string RouteId, string Template, string ClusterId, int Order);

/// <summary>
/// The Gateway's YARP routes, copied from <c>src/Dhadgar.Gateway/appsettings.json</c> <c>ReverseProxy:Routes</c>. Each route carries its
/// <c>Match:Path</c> template and its <c>Order</c>, and <see cref="ClusterFor"/> answers like YARP: the lowest Order wins and ties go to
/// table order. <see cref="GatewayRouteTableTests"/> fails when a route is added, removed or changed on the Gateway and not here.
/// </summary>
public static class GatewayRouteTable
{
    /// <summary>
    /// The Gateway's routes, in <c>appsettings.json</c> order.
    /// </summary>
    public static IReadOnlyList<GatewayRoute> Routes { get; } =
    [
        new("identity-internal-block", "/api/v1/identity/internal/{**catch-all}", "identity", 1),
        new("nodes-reservations-block", "/api/v1/organizations/{organizationId}/nodes/{nodeId}/reservations/{**catch-all}", "nodes", 1),
        new("betterauth-route", "/api/v1/betterauth/{**catch-all}", "betterauth", 10),
        new("identity-route", "/api/v1/identity/{**catch-all}", "identity", 10),
        new("servers-org-route", "/api/v1/organizations/{organizationId}/servers/{**catch-all}", "servers", 20),
        new("nodes-org-route", "/api/v1/organizations/{organizationId}/nodes/{**catch-all}", "nodes", 20),
        new("enrollment-route", "/api/v1/organizations/{organizationId}/enrollment/{**catch-all}", "nodes", 20),
        new("console-org-route", "/api/v1/organizations/{organizationId}/servers/{serverId}/console/{**catch-all}", "console", -1),
        new("console-hub-route", "/hubs/console/{**catch-all}", "console", 20),
        new("agent-hub-route", "/hubs/agent/{**catch-all}", "nodes", 20),
        new("notifications-route", "/api/v1/notifications/{**catch-all}", "notifications", 20),
        new("secrets-route", "/api/v1/secrets/{**catch-all}", "secrets", 20),
        new("discord-route", "/api/v1/discord/{**catch-all}", "discord", 20),
        new("agents-ca-route", "/api/v1/agents/ca-certificate", "nodes", 22),
        new("agents-enroll-route", "/api/v1/agents/enroll", "nodes", 25),
        new("agents-route", "/api/v1/agents/{**catch-all}", "nodes", 30),
    ];

    /// <summary>
    /// The routes in YARP match order: ascending Order, ties in table order (LINQ <c>OrderBy</c> is stable).
    /// </summary>
    private static readonly IReadOnlyList<GatewayRoute> ByOrder = Routes.OrderBy(route => route.Order).ToList();

    /// <summary>
    /// The cluster a request path routes to, or null when no route matches (the Gateway answers 404 for it). Null, empty and
    /// unmatched input return null; this method never throws.
    /// </summary>
    public static string? ClusterFor(string? requestPath)
    {
        if (string.IsNullOrEmpty(requestPath))
        {
            return null;
        }

        int query = requestPath.IndexOf('?', StringComparison.Ordinal);
        string path = query >= 0 ? requestPath[..query] : requestPath;
        foreach (GatewayRoute route in ByOrder)
        {
            if (Matches(route.Template, path))
            {
                return route.ClusterId;
            }
        }

        return null;
    }

    /// <summary>
    /// Matches a path against an ASP.NET-style template, the subset YARP uses: <c>{name}</c> matches one segment, a final
    /// <c>{**name}</c> matches zero or more remaining segments, and any other segment is a literal compared case-insensitively.
    /// </summary>
    internal static bool Matches(string template, string path)
    {
        string[] templateSegments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < templateSegments.Length; i++)
        {
            string segment = templateSegments[i];
            bool isParameter = segment.StartsWith('{') && segment.EndsWith('}');
            if (isParameter && segment.StartsWith("{**", StringComparison.Ordinal))
            {
                // A catch-all must be the last template segment; anything else is not a template this table uses.
                return i == templateSegments.Length - 1;
            }

            if (i >= pathSegments.Length)
            {
                return false;
            }

            if (!isParameter && !string.Equals(segment, pathSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return templateSegments.Length == pathSegments.Length;
    }
}
