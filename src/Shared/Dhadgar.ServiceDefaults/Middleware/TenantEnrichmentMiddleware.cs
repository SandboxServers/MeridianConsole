using Dhadgar.ServiceDefaults.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Middleware;

/// <summary>
/// Middleware that adds tenant and service context to the logging scope for all downstream operations.
/// This ensures every log entry within an HTTP request automatically includes TenantId, CorrelationId,
/// RequestId, ServiceName, ServiceVersion, and Hostname.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPORTANT:</b> This middleware MUST run after <see cref="CorrelationMiddleware"/> in the pipeline.
/// The CorrelationMiddleware sets the CorrelationId and RequestId in HttpContext.Items, which this
/// middleware reads and adds to the logging scope.
/// </para>
/// <para>
/// Recommended middleware order:
/// <list type="number">
///   <item><see cref="CorrelationMiddleware"/> - Sets CorrelationId and RequestId</item>
///   <item><see cref="TenantEnrichmentMiddleware"/> - Adds all context to logging scope</item>
///   <item><see cref="RequestLoggingMiddleware"/> - Logs requests with full context</item>
/// </list>
/// </para>
/// <para>
/// All logs within the scope automatically include these fields without explicit logging:
/// <list type="bullet">
///   <item><b>TenantId</b>: The organization ID from the current request context, or "system" if not available</item>
///   <item><b>CorrelationId</b>: The correlation ID for distributed tracing</item>
///   <item><b>RequestId</b>: The unique request identifier</item>
///   <item><b>ServiceName</b>: The name of the service (e.g., "Dhadgar.Gateway")</item>
///   <item><b>ServiceVersion</b>: The assembly version of the service</item>
///   <item><b>Hostname</b>: The machine name where the service is running</item>
/// </list>
/// </para>
/// <para>
/// For background services (not HTTP requests), create a scope manually with the same fields:
/// <code>
/// using (_logger.BeginScope(new Dictionary&lt;string, object&gt;
/// {
///     ["TenantId"] = tenantId,
///     ["CorrelationId"] = correlationId,
///     ["ServiceName"] = TenantEnrichmentMiddleware.ServiceInfo.Name,
///     ["ServiceVersion"] = TenantEnrichmentMiddleware.ServiceInfo.Version,
///     ["Hostname"] = TenantEnrichmentMiddleware.ServiceInfo.Hostname
/// }))
/// {
///     // Your background work here - all logs include context
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class TenantEnrichmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantEnrichmentMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantEnrichmentMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    public TenantEnrichmentMiddleware(RequestDelegate next, ILogger<TenantEnrichmentMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware, wrapping the request in a logging scope with tenant and service context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="organizationContext">The scoped organization context service.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context, IOrganizationContext organizationContext)
    {
        // Get correlation context from CorrelationMiddleware (set via HttpContext.Items)
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";
        var requestId = context.Items["RequestId"]?.ToString() ?? "unknown";

        // Get tenant context from IOrganizationContext (resolved from claims or headers)
        var tenantId = organizationContext.OrganizationId?.ToString() ?? "system";

        // Get cached service info (computed once per process)
        var info = ServiceInfo;

        // Create logging scope with all context - all logs within this scope automatically include these fields
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenantId,
            ["CorrelationId"] = correlationId,
            ["RequestId"] = requestId,
            ["ServiceName"] = info.Name,
            ["ServiceVersion"] = info.Version,
            ["Hostname"] = info.Hostname
        }))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Cached service information (computed once per process).
    /// Exposed publicly for use by background services that need to create their own logging scopes.
    /// </summary>
    public static ServiceInfoData ServiceInfo => _serviceInfo.Value;

    private static readonly Lazy<ServiceInfoData> _serviceInfo = new(GetServiceInfo);

    /// <summary>
    /// Gets service information from the entry assembly and environment.
    /// This is computed once and cached for the lifetime of the process.
    /// </summary>
    private static ServiceInfoData GetServiceInfo()
    {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        var serviceName = entryAssembly?.GetName().Name ?? "Unknown";
        var serviceVersion = entryAssembly?.GetName().Version?.ToString() ?? "1.0.0";
        var hostname = Environment.MachineName;

        return new ServiceInfoData(serviceName, serviceVersion, hostname);
    }

    /// <summary>
    /// Holds cached service information to avoid reflection on every request.
    /// </summary>
    /// <param name="Name">The service name (e.g., "Dhadgar.Gateway").</param>
    /// <param name="Version">The service version (e.g., "1.0.0").</param>
    /// <param name="Hostname">The machine hostname where the service is running.</param>
    public sealed record ServiceInfoData(string Name, string Version, string Hostname);
}
