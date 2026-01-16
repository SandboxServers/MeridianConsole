using System.Linq;

namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that enriches requests with headers for backend services.
/// Extracts tenant and user information from JWT (when available).
///
/// SECURITY: Strips client-supplied security headers to prevent spoofing.
/// </summary>
public class RequestEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    // Headers that MUST be stripped to prevent client spoofing
    private static readonly string[] SecurityHeaders = new[]
    {
        "X-Tenant-Id",
        "X-User-Id",
        "X-Client-Type",
        "X-Agent-Id",
        "X-Roles"
    };

    public RequestEnrichmentMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // CRITICAL: Strip all security headers sent by client FIRST
        // This prevents header spoofing attacks where a client could send
        // X-Tenant-Id: some-other-tenant-id to access another tenant's data
        foreach (var header in SecurityHeaders)
        {
            context.Request.Headers.Remove(header);
        }

        // Ensure request ID exists
        if (!context.Request.Headers.TryGetValue("X-Request-Id", out var requestIds) ||
            string.IsNullOrWhiteSpace(requestIds.ToString()))
        {
            context.Request.Headers["X-Request-Id"] =
                context.Items["RequestId"]?.ToString() ?? Guid.NewGuid().ToString("N");
        }

        var requestId = context.Request.Headers["X-Request-Id"].ToString();

        // Extract tenant/org from JWT and inject (if authenticated)
        // Now safe because we stripped any client-supplied X-Tenant-Id above
        // Note: JWT uses "org_id" claim (see TokenExchangeService)
        var tenantId = context.User.FindFirst("org_id")?.Value;
        if (!string.IsNullOrEmpty(tenantId))
        {
            context.Request.Headers["X-Tenant-Id"] = tenantId;
        }

        // Extract user ID from JWT and inject (if authenticated)
        var userId = context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            context.Request.Headers["X-User-Id"] = userId;
        }

        // Extract client type (user vs agent)
        var clientType = context.User.FindFirst("client_type")?.Value;
        if (!string.IsNullOrEmpty(clientType))
        {
            context.Request.Headers["X-Client-Type"] = clientType;
        }

        // Add client IP for backend services
        var clientIp = GetClientIpAddress(context);
        if (!string.IsNullOrEmpty(clientIp))
        {
            context.Request.Headers["X-Real-IP"] = clientIp;
        }

        // Add request ID to response
        context.Response.Headers["X-Request-Id"] = requestId;

        await _next(context);
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded header (behind Cloudflare/proxy)
        var forwardedFor = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP (original client)
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
