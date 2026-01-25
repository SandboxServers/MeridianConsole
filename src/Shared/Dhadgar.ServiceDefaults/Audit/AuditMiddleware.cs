using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Dhadgar.ServiceDefaults.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Audit;

/// <summary>
/// Middleware that captures authenticated HTTP requests for audit logging.
/// </summary>
/// <remarks>
/// <para>
/// This middleware captures API requests for compliance and analysis. It only audits
/// <b>authenticated</b> requests (per AUDIT-01: "authenticated API calls").
/// </para>
/// <para>
/// <b>Registration order:</b> This middleware MUST run after authentication middleware
/// so that <c>context.User.Identity.IsAuthenticated</c> is properly set.
/// </para>
/// <para>
/// Recommended middleware order:
/// <list type="number">
///   <item><see cref="CorrelationMiddleware"/> - Sets CorrelationId and RequestId</item>
///   <item><see cref="TenantEnrichmentMiddleware"/> - Adds tenant context to logging scope</item>
///   <item><see cref="RequestLoggingMiddleware"/> - Logs requests</item>
///   <item>Authentication/Authorization middleware</item>
///   <item><see cref="AuditMiddleware"/> - Audits authenticated requests (this middleware)</item>
/// </list>
/// </para>
/// <para>
/// Skipped endpoints:
/// <list type="bullet">
///   <item>Unauthenticated requests</item>
///   <item>Health check endpoints: /healthz, /livez, /readyz</item>
/// </list>
/// </para>
/// <para>
/// <b>Non-blocking:</b> Audit records are queued to <see cref="IAuditQueue"/> without waiting.
/// The <see cref="AuditWriterService{TContext}"/> drains the queue in the background.
/// </para>
/// </remarks>
public sealed partial class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuditQueue _auditQueue;
    private readonly ILogger<AuditMiddleware> _logger;

    /// <summary>
    /// Regex pattern to extract resource type and ID from common API paths.
    /// Matches patterns like /api/v1/servers/guid or /servers/guid.
    /// </summary>
    private static readonly Regex ResourceIdPattern = ResourceIdRegex();

    /// <summary>
    /// Cached service name from the entry assembly.
    /// </summary>
    private static readonly string ServiceName = TenantEnrichmentMiddleware.ServiceInfo.Name;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="auditQueue">The audit queue for non-blocking writes.</param>
    /// <param name="logger">The logger instance.</param>
    public AuditMiddleware(
        RequestDelegate next,
        IAuditQueue auditQueue,
        ILogger<AuditMiddleware> logger)
    {
        _next = next;
        _auditQueue = auditQueue;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware, capturing the request for audit if authenticated.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip unauthenticated requests (per AUDIT-01: "authenticated API calls")
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Skip health check endpoints
        var path = context.Request.Path.Value ?? string.Empty;
        if (IsHealthEndpoint(path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var timestampUtc = DateTime.UtcNow;

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var record = BuildAuditRecord(context, timestampUtc, stopwatch.ElapsedMilliseconds);

            // Fire-and-forget queue (non-blocking)
            // Discard the task to avoid blocking the request thread
            // CA2012 suppressed: Intentional fire-and-forget per audit architecture
#pragma warning disable CA2012 // Use ValueTasks correctly
            _ = _auditQueue.QueueAsync(record);
#pragma warning restore CA2012
        }
    }

    /// <summary>
    /// Checks if the path is a health check endpoint that should be skipped.
    /// </summary>
    private static bool IsHealthEndpoint(string path)
    {
        return path.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/livez", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/readyz", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds an audit record from the HTTP context.
    /// </summary>
    private static ApiAuditRecord BuildAuditRecord(HttpContext context, DateTime timestamp, long durationMs)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var (resourceType, resourceId) = ExtractResourceInfo(path);

        return new ApiAuditRecord
        {
            TimestampUtc = timestamp,
            UserId = ExtractUserId(context),
            TenantId = ExtractTenantId(context),
            HttpMethod = context.Request.Method,
            Path = path,
            ResourceId = resourceId,
            ResourceType = resourceType,
            StatusCode = context.Response.StatusCode,
            DurationMs = durationMs,
            ClientIp = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers.UserAgent.ToString().Truncate(256),
            CorrelationId = context.Items["CorrelationId"]?.ToString(),
            TraceId = Activity.Current?.TraceId.ToString(),
            ServiceName = ServiceName
        };
    }

    /// <summary>
    /// Extracts the user ID from JWT claims.
    /// </summary>
    private static Guid? ExtractUserId(HttpContext context)
    {
        // Try standard "sub" claim first, then NameIdentifier
        var subClaim = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subClaim, out var userId) ? userId : null;
    }

    /// <summary>
    /// Extracts the tenant/organization ID from JWT claims.
    /// </summary>
    private static Guid? ExtractTenantId(HttpContext context)
    {
        // Try "org_id" first (common convention), then "tenant_id"
        var tenantClaim = context.User.FindFirst("org_id")?.Value
            ?? context.User.FindFirst("tenant_id")?.Value;

        return Guid.TryParse(tenantClaim, out var tenantId) ? tenantId : null;
    }

    /// <summary>
    /// Extracts resource type and ID from the request path.
    /// </summary>
    private static (string? ResourceType, Guid? ResourceId) ExtractResourceInfo(string path)
    {
        var match = ResourceIdPattern.Match(path);

        if (!match.Success)
        {
            return (null, null);
        }

        var resourceType = match.Groups[1].Value.ToLowerInvariant();
        var resourceId = Guid.TryParse(match.Groups[2].Value, out var id) ? id : (Guid?)null;

        return (resourceType, resourceId);
    }

    /// <summary>
    /// Source-generated regex for extracting resource type and ID from API paths.
    /// Matches patterns like /api/v1/servers/guid or /servers/guid.
    /// </summary>
    [GeneratedRegex(
        @"/(?:api/)?v?\d*/?(servers|nodes|users|organizations|tasks|files|mods)/([0-9a-fA-F-]{36})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ResourceIdRegex();
}

/// <summary>
/// String extension methods for audit middleware.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Truncates a string to the specified maximum length.
    /// </summary>
    /// <param name="value">The string to truncate.</param>
    /// <param name="maxLength">The maximum length.</param>
    /// <returns>The truncated string, or null if the input was null.</returns>
    public static string? Truncate(this string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
