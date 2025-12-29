using System.Diagnostics;

namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that ensures every request has correlation and request IDs for distributed tracing.
/// </summary>
public class CorrelationMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string RequestIdHeader = "X-Request-Id";

    public CorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract or generate correlation ID
        var correlationId = GetOrCreateCorrelationId(context);
        var requestId = Guid.NewGuid().ToString("N");

        // Set on current activity for trace correlation
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetTag("request.id", requestId);
        Activity.Current?.SetBaggage("correlation.id", correlationId);

        // Add to response headers
        context.Response.Headers[CorrelationIdHeader] = correlationId;
        context.Response.Headers[RequestIdHeader] = requestId;

        // Also include trace ID for debugging
        var activity = Activity.Current;
        if (activity != null)
        {
            context.Response.Headers["X-Trace-Id"] = activity.TraceId.ToString();
        }

        // Store in HttpContext.Items for downstream access
        context.Items["CorrelationId"] = correlationId;
        context.Items["RequestId"] = requestId;

        await _next(context);
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        // Priority: X-Correlation-Id header > traceparent trace ID > new GUID
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationHeader)
            && !string.IsNullOrWhiteSpace(correlationHeader))
        {
            return correlationHeader.ToString();
        }

        var activity = Activity.Current;
        if (activity != null && activity.TraceId != default)
        {
            return activity.TraceId.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }
}
