namespace Dhadgar.Gateway.Services;

/// <summary>Decides which proxied routes get the gateway-wide <c>Global</c> rate limiter.</summary>
public static class RouteRateLimiting
{
    public const string GlobalPolicy = "Global";

    // ASP.NET Core enforces only the last EnableRateLimiting metadata on an endpoint, so attaching Global to a
    // route silently disables the route's own policy. Policies listed here replace Global instead.
    // Only PerAgent for now (P06-ruling-2); Auth and PerTenant stay Global-only until the owner rules (D-S220).
    private static readonly HashSet<string> PoliciesThatReplaceGlobal = new(StringComparer.Ordinal) { "PerAgent" };

    /// <summary>True when a route with this <c>RateLimiterPolicy</c> should get <c>Global</c> attached.</summary>
    public static bool UsesGlobal(string? routePolicy) =>
        routePolicy is null || !PoliciesThatReplaceGlobal.Contains(routePolicy);
}
