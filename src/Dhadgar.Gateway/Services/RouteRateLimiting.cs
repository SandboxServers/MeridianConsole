namespace Dhadgar.Gateway.Services;

/// <summary>Decides which proxied routes get the gateway-wide <c>Global</c> rate limiter.</summary>
public static class RouteRateLimiting
{
    public const string GlobalPolicy = "Global";

    // ASP.NET Core enforces only the last EnableRateLimiting metadata on an endpoint, so attaching Global to a
    // route that names a policy would silently replace that policy. Global is only the fallback for routes that
    // name none, or name YARP's "Default" (D-S228).
    /// <summary>True when a route with this <c>RateLimiterPolicy</c> should get <c>Global</c> attached.</summary>
    public static bool UsesGlobal(string? routePolicy) =>
        string.IsNullOrWhiteSpace(routePolicy)
        || string.Equals(routePolicy, "Default", StringComparison.OrdinalIgnoreCase);
}
