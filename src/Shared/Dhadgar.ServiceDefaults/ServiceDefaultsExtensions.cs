using Dhadgar.ServiceDefaults.Logging;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhadgar.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Adds Dhadgar service defaults including health checks, organization context, and request logging services.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers:
    /// <list type="bullet">
    ///   <item>Health checks with "self" check for liveness</item>
    ///   <item><see cref="IOrganizationContext"/> for multi-tenant scenarios</item>
    ///   <item><see cref="RequestLoggingMessages"/> for source-generated HTTP logging</item>
    /// </list>
    /// </para>
    /// <para>
    /// After calling this method, use <see cref="UseDhadgarMiddleware"/> on the WebApplication
    /// to register the middleware pipeline in the correct order.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDhadgarServiceDefaults(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        // Register organization context for multi-tenant logging
        services.AddOrganizationContext();

        // Register source-generated request logging messages as singleton
        services.AddSingleton<RequestLoggingMessages>();

        return services;
    }

    public static WebApplication MapDhadgarDefaultEndpoints(this WebApplication app)
    {
        Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
        {
            var payload = new Dictionary<string, object?>
            {
                ["service"] = app.Environment.ApplicationName,
                ["status"] = report.Status == HealthStatus.Healthy ? "ok" : "unhealthy",
                ["timestamp"] = DateTime.UtcNow
            };

            if (report.Entries.Count > 0)
            {
                var checks = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in report.Entries)
                {
                    var entryPayload = new Dictionary<string, object?>
                    {
                        ["status"] = entry.Value.Status.ToString(),
                        ["duration_ms"] = entry.Value.Duration.TotalMilliseconds
                    };

                    if (!string.IsNullOrWhiteSpace(entry.Value.Description))
                    {
                        entryPayload["description"] = entry.Value.Description;
                    }

                    if (entry.Value.Data.Count > 0)
                    {
                        entryPayload["data"] = entry.Value.Data;
                    }

                    checks[entry.Key] = entryPayload;
                }

                payload["checks"] = checks;
            }

            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(payload);
        }

        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        app.MapHealthChecks("/livez", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponseAsync
        })
        .AllowAnonymous()
        .WithTags("Health");

        return app;
    }

    /// <summary>
    /// Registers the Dhadgar middleware pipeline in the correct order.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <returns>The web application for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers middleware in the following order:
    /// <list type="number">
    ///   <item><see cref="CorrelationMiddleware"/> - Sets CorrelationId and RequestId for distributed tracing</item>
    ///   <item><see cref="TenantEnrichmentMiddleware"/> - Adds TenantId, ServiceName, ServiceVersion, Hostname to logging scope</item>
    ///   <item><see cref="RequestLoggingMiddleware"/> - Logs HTTP requests/responses with full context</item>
    /// </list>
    /// </para>
    /// <para>
    /// The order is critical:
    /// <list type="bullet">
    ///   <item><see cref="CorrelationMiddleware"/> MUST run first to establish correlation IDs</item>
    ///   <item><see cref="TenantEnrichmentMiddleware"/> MUST run second to create the logging scope with all context</item>
    ///   <item><see cref="RequestLoggingMiddleware"/> MUST run third to log within the established scope</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method replaces manual middleware registration. Instead of:
    /// <code>
    /// app.UseMiddleware&lt;CorrelationMiddleware&gt;();
    /// app.UseMiddleware&lt;TenantEnrichmentMiddleware&gt;();
    /// app.UseMiddleware&lt;RequestLoggingMiddleware&gt;();
    /// </code>
    /// Use:
    /// <code>
    /// app.UseDhadgarMiddleware();
    /// </code>
    /// </para>
    /// <para>
    /// Call this early in the pipeline, typically after exception handling and HTTPS redirection
    /// but before routing and authentication:
    /// <code>
    /// var app = builder.Build();
    ///
    /// app.UseExceptionHandler();
    /// app.UseHttpsRedirection();
    /// app.UseDhadgarMiddleware(); // Add here
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.MapControllers();
    /// </code>
    /// </para>
    /// </remarks>
    public static WebApplication UseDhadgarMiddleware(this WebApplication app)
    {
        // 1. CorrelationMiddleware - Sets CorrelationId and RequestId in HttpContext.Items
        //    These IDs are used for distributed tracing across services
        app.UseMiddleware<CorrelationMiddleware>();

        // 2. TenantEnrichmentMiddleware - Adds all context to logging scope
        //    Reads CorrelationId/RequestId from HttpContext.Items (set by CorrelationMiddleware)
        //    Reads TenantId from IOrganizationContext (from claims or headers)
        //    Adds ServiceName, ServiceVersion, Hostname from cached service info
        app.UseMiddleware<TenantEnrichmentMiddleware>();

        // 3. RequestLoggingMiddleware - Logs HTTP requests with full context
        //    Uses source-generated RequestLoggingMessages for high-performance logging
        //    All log entries automatically include context from TenantEnrichmentMiddleware scope
        app.UseMiddleware<RequestLoggingMiddleware>();

        return app;
    }
}
