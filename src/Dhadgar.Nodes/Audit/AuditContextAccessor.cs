using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Audit;

/// <summary>
/// Captures audit context from the current HTTP request.
/// Used by AuditService to automatically populate context fields.
/// </summary>
public interface IAuditContextAccessor
{
    /// <summary>Get the current actor ID from the request context.</summary>
    string GetActorId();

    /// <summary>Get the current actor type from the request context.</summary>
    ActorType GetActorType();

    /// <summary>Get the correlation ID from the request context.</summary>
    string? GetCorrelationId();

    /// <summary>Get the request ID from the request context.</summary>
    string? GetRequestId();

    /// <summary>Get the client IP address.</summary>
    string? GetIpAddress();

    /// <summary>Get the client user agent.</summary>
    string? GetUserAgent();
}

/// <summary>
/// Default implementation that extracts audit context from HttpContext.
/// </summary>
public sealed class AuditContextAccessor : IAuditContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetActorId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return "system";
        }

        // Check for authenticated user
        var userId = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst("client_id")?.Value
            ?? httpContext.User.Identity?.Name;

        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        // Check for agent authentication (certificate-based)
        var nodeId = httpContext.User.FindFirst("node_id")?.Value;
        if (!string.IsNullOrEmpty(nodeId))
        {
            return $"agent:{nodeId}";
        }

        // Check for service account
        var serviceName = httpContext.Request.Headers["X-Service-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(serviceName))
        {
            return $"service:{serviceName}";
        }

        return "anonymous";
    }

    public ActorType GetActorType()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return ActorType.System;
        }

        // Check for agent authentication
        var nodeId = httpContext.User.FindFirst("node_id")?.Value;
        if (!string.IsNullOrEmpty(nodeId))
        {
            return ActorType.Agent;
        }

        // Check for service account
        var serviceName = httpContext.Request.Headers["X-Service-Name"].FirstOrDefault();
        if (!string.IsNullOrEmpty(serviceName))
        {
            return ActorType.Service;
        }

        // Check for authenticated user
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return ActorType.User;
        }

        return ActorType.System;
    }

    public string? GetCorrelationId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        // Try to get from the correlation middleware context first (stored as "CorrelationId")
        if (httpContext.Items.TryGetValue("CorrelationId", out var correlationId))
        {
            return correlationId?.ToString();
        }

        // Fallback to header
        return httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    }

    public string? GetRequestId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        // Try to get from the correlation middleware context first (stored as "RequestId")
        if (httpContext.Items.TryGetValue("RequestId", out var requestId))
        {
            return requestId?.ToString();
        }

        // Fallback to header
        return httpContext.Request.Headers["X-Request-Id"].FirstOrDefault();
    }

    public string? GetIpAddress()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        // Check X-Forwarded-For header (when behind proxy/load balancer)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP (original client)
            var firstIp = forwardedFor.Split(',')[0].Trim();
            return firstIp;
        }

        // Check X-Real-IP header (nginx style)
        var realIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fallback to connection remote IP
        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    public string? GetUserAgent()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        return httpContext?.Request.Headers.UserAgent.FirstOrDefault();
    }
}
