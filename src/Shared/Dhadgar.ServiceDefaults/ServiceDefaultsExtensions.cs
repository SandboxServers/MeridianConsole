using Dhadgar.ServiceDefaults.Health;
using Dhadgar.ServiceDefaults.Logging;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.MultiTenancy;
using Dhadgar.ServiceDefaults.Serialization;
using Dhadgar.ServiceDefaults.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

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
    ///   <item>Strict JSON serialization (rejects duplicate properties, uses camelCase)</item>
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

        // Configure strict JSON serialization for security hardening
        services.AddStrictJsonSerialization();

        return services;
    }

    /// <summary>
    /// Adds Dhadgar service defaults with configurable health check dependencies and OpenTelemetry instrumentation.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Configuration to read connection strings and OTLP endpoint from.</param>
    /// <param name="dependencies">Flags indicating which dependencies to check for readiness.</param>
    /// <param name="configureTracing">Optional callback for additional tracing configuration (e.g., Redis instrumentation).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This overload registers:
    /// <list type="bullet">
    ///   <item>Health checks based on specified dependencies</item>
    ///   <item>OpenTelemetry tracing with ASP.NET Core, HTTP client, and EF Core instrumentation</item>
    ///   <item>OpenTelemetry metrics with runtime and process instrumentation</item>
    ///   <item>Logging infrastructure with PII redaction</item>
    ///   <item>Organization context for multi-tenant scenarios</item>
    ///   <item>Strict JSON serialization (rejects duplicate properties, uses camelCase)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Health checks are tagged for Kubernetes probes:
    /// <list type="bullet">
    ///   <item>"live" tag - For liveness probes (/livez), includes only the self check</item>
    ///   <item>"ready" tag - For readiness probes (/readyz), includes dependency checks</item>
    /// </list>
    /// </para>
    /// <para>
    /// Configuration keys:
    /// <list type="bullet">
    ///   <item><c>OpenTelemetry:OtlpEndpoint</c> - OTLP collector endpoint (e.g., "http://localhost:4317")</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDhadgarServiceDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        HealthCheckDependencies dependencies,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        var healthChecks = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        if (dependencies.HasFlag(HealthCheckDependencies.Postgres))
        {
            var connectionString = configuration.GetConnectionString("Postgres");
            if (!string.IsNullOrEmpty(connectionString))
            {
                healthChecks.AddNpgSql(
                    connectionString,
                    name: "postgres",
                    timeout: TimeSpan.FromSeconds(3),
                    tags: ["ready"]);
            }
        }

        if (dependencies.HasFlag(HealthCheckDependencies.Redis))
        {
            var connectionString = configuration["Redis:ConnectionString"];
            if (!string.IsNullOrEmpty(connectionString))
            {
                healthChecks.AddRedis(
                    connectionString,
                    name: "redis",
                    timeout: TimeSpan.FromSeconds(2),
                    tags: ["ready"]);
            }
        }

        if (dependencies.HasFlag(HealthCheckDependencies.RabbitMq))
        {
            var rabbitHost = configuration["RabbitMq:Host"] ?? "localhost";
            var rabbitUser = configuration["RabbitMq:Username"] ?? "dhadgar";
            var rabbitPass = configuration["RabbitMq:Password"] ?? "dhadgar";

            // RabbitMQ.Client 7.x uses async connection factory
            // The health check library requires an async factory that returns IConnection
            healthChecks.AddRabbitMQ(
                factory: async _ =>
                {
                    var connectionFactory = new ConnectionFactory
                    {
                        HostName = rabbitHost,
                        UserName = rabbitUser,
                        Password = rabbitPass
                    };
                    return await connectionFactory.CreateConnectionAsync();
                },
                name: "rabbitmq",
                timeout: TimeSpan.FromSeconds(3),
                tags: ["ready"]);
        }

        // Register organization context for multi-tenant logging
        services.AddOrganizationContext();

        // Register source-generated request logging messages as singleton
        services.AddSingleton<RequestLoggingMessages>();

        // Add logging infrastructure with PII redaction
        services.AddDhadgarLogging();

        // Configure strict JSON serialization for security hardening
        services.AddStrictJsonSerialization();

        // Configure OpenTelemetry (tracing + metrics)
        ConfigureOpenTelemetry(services, configuration, configureTracing);

        return services;
    }

    /// <summary>
    /// Configures OpenTelemetry tracing, metrics, and prepares logging integration.
    /// </summary>
    private static void ConfigureOpenTelemetry(
        IServiceCollection services,
        IConfiguration configuration,
        Action<TracerProviderBuilder>? configureTracing)
    {
        // Get OTLP endpoint from configuration
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        Uri? otlpUri = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsed))
        {
            otlpUri = parsed;
        }

        // Get service name from entry assembly
        var serviceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
        var serviceVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        // Build resource with service name and version
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion);

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);

                // Add ASP.NET Core instrumentation for HTTP request/response spans
                tracing.AddAspNetCoreInstrumentation();

                // Add HTTP client instrumentation for outbound calls
                tracing.AddHttpClientInstrumentation();

                // Add Entity Framework Core instrumentation with db.system enrichment
                tracing.AddEntityFrameworkCoreInstrumentation(options =>
                {
                    options.SetDbStatementForText = true;
                    options.SetDbStatementForStoredProcedure = true;
                    options.EnrichWithIDbCommand = (activity, command) =>
                    {
                        activity.SetTag(TracingConstants.Attributes.DbSystem, TracingConstants.DatabaseSystem);
                    };
                });

                // Add shared Dhadgar ActivitySource for custom business spans
                tracing.AddSource(DhadgarActivitySource.Name);

                // Allow service-specific configuration (Redis, custom sources, etc.)
                configureTracing?.Invoke(tracing);

                // Add OTLP exporter if endpoint is configured
                if (otlpUri is not null)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);

                // Add ASP.NET Core metrics (request duration, active requests, etc.)
                metrics.AddAspNetCoreInstrumentation();

                // Add HTTP client metrics (outbound request duration, etc.)
                metrics.AddHttpClientInstrumentation();

                // Add runtime metrics (GC, thread pool, etc.)
                metrics.AddRuntimeInstrumentation();

                // Add process metrics (CPU, memory, etc.)
                metrics.AddProcessInstrumentation();

                // Add OTLP exporter if endpoint is configured
                if (otlpUri is not null)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
                }
            });
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
