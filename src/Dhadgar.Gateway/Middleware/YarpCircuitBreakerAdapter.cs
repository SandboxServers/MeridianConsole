using Yarp.ReverseProxy.Model;

namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Adapter middleware that extracts YARP cluster information and sets it
/// in HttpContext.Items for the shared CircuitBreakerMiddleware to use.
/// </summary>
public class YarpCircuitBreakerAdapter
{
    private readonly RequestDelegate _next;

    public YarpCircuitBreakerAdapter(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get cluster ID from YARP feature (set by the proxy)
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var clusterId = proxyFeature?.Cluster?.Config.ClusterId;

        if (!string.IsNullOrEmpty(clusterId))
        {
            context.Items["CircuitBreaker:ServiceId"] = clusterId;
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for the YARP circuit breaker adapter.
/// </summary>
public static class YarpCircuitBreakerAdapterExtensions
{
    /// <summary>
    /// Adds the YARP circuit breaker adapter middleware.
    /// This should be added before UseCircuitBreaker() when using YARP.
    /// </summary>
    public static IApplicationBuilder UseYarpCircuitBreakerAdapter(this IApplicationBuilder app)
    {
        return app.UseMiddleware<YarpCircuitBreakerAdapter>();
    }
}
