using Dhadgar.ServiceDefaults.Audit;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace Dhadgar.ServiceDefaults;

/// <summary>
/// Extension methods for configuring Dhadgar services with Aspire integration.
/// </summary>
public static class ServiceDefaultsExtensions
{
    #region Aspire-Integrated Methods (Primary API)

    /// <summary>
    /// Adds Dhadgar service defaults with Aspire-compatible patterns.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method provides the complete service defaults configuration compatible with .NET Aspire:
    /// <list type="bullet">
    ///   <item>Configures OpenTelemetry (tracing, metrics, logging) with OTLP export</item>
    ///   <item>Adds health checks with liveness/readiness tagging</item>
    ///   <item>Adds Dhadgar multi-tenant organization context</item>
    ///   <item>Configures strict JSON serialization</item>
    ///   <item>Adds PII redaction to logging</item>
    ///   <item>Registers source-generated request logging</item>
    ///   <item>Adds custom DhadgarActivitySource for business-level spans</item>
    /// </list>
    /// </para>
    /// <para>
    /// When running under Aspire orchestration, the OTLP endpoint and other configuration
    /// is automatically set via environment variables by the AppHost.
    /// </para>
    /// <para>
    /// After calling this method, use <see cref="MapDhadgarDefaults"/> on the WebApplication
    /// to register endpoints and middleware.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddDhadgarServiceDefaults(this IHostApplicationBuilder builder)
    {
        // 1. Configure OpenTelemetry (tracing, metrics, logging)
        ConfigureOpenTelemetry(builder);

        // 2. Add health checks with liveness tag
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        // 3. Add Dhadgar-specific services
        builder.Services.AddOrganizationContext();
        builder.Services.AddSingleton<RequestLoggingMessages>();
        builder.Services.AddStrictJsonSerialization();

        // 4. Add PII redaction to logging
        builder.Services.AddDhadgarLogging();

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry with Aspire-compatible defaults.
    /// </summary>
    private static void ConfigureOpenTelemetry(IHostApplicationBuilder builder)
    {
        // Get OTLP endpoint from environment or configuration
        // Aspire sets OTEL_EXPORTER_OTLP_ENDPOINT automatically
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? builder.Configuration["OpenTelemetry:OtlpEndpoint"];

        Uri? otlpUri = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsed))
        {
            otlpUri = parsed;
        }

        var serviceName = builder.Environment.ApplicationName;
        var serviceVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion);

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();

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

                if (otlpUri is not null)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddProcessInstrumentation();

                if (otlpUri is not null)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
                }
            });

        // Configure logging to include formatted messages and scopes
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
    }

    /// <summary>
    /// Adds Dhadgar service defaults with explicit health check dependencies.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    /// <param name="dependencies">Flags indicating which infrastructure dependencies to add health checks for.</param>
    /// <param name="configureTracing">Optional callback for additional tracing configuration.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Use this overload when you need service-specific health checks. When running under Aspire,
    /// the AppHost automatically wires health checks for resources via WithReference(). Use this
    /// overload for services running standalone or when you need explicit health check configuration.
    /// </remarks>
    public static IHostApplicationBuilder AddDhadgarServiceDefaults(
        this IHostApplicationBuilder builder,
        HealthCheckDependencies dependencies,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        // Call base configuration
        builder.AddDhadgarServiceDefaults();

        // Add additional health checks based on flags
        var healthChecks = builder.Services.AddHealthChecks();

        if (dependencies.HasFlag(HealthCheckDependencies.Postgres))
        {
            var connectionString = builder.Configuration.GetConnectionString("Postgres");
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
            var connectionString = builder.Configuration["Redis:ConnectionString"];
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
            var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
            var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "dhadgar";
            var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "dhadgar";

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

        // Allow service-specific tracing configuration
        if (configureTracing is not null)
        {
            builder.Services.ConfigureOpenTelemetryTracerProvider(configureTracing);
        }

        return builder;
    }

    /// <summary>
    /// Maps Dhadgar default endpoints and middleware.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <param name="options">Optional middleware configuration options.</param>
    /// <returns>The application for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method:
    /// <list type="bullet">
    ///   <item>Maps health endpoints (/health, /alive) - compatible with Aspire expectations</item>
    ///   <item>Maps Kubernetes-style health endpoints (/healthz, /livez, /readyz)</item>
    ///   <item>Registers Dhadgar middleware pipeline (correlation, tenant, logging)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static WebApplication MapDhadgarDefaults(this WebApplication app, DhadgarServiceOptions? options = null)
    {
        // Map Aspire-compatible health endpoints (/health, /alive)
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        // Map Kubernetes-style health endpoints
        app.MapDhadgarDefaultEndpoints();

        // Register Dhadgar middleware pipeline
        app.UseDhadgarMiddleware(options);

        return app;
    }

    #endregion

    #region Legacy IServiceCollection Methods (Backward Compatibility)

    /// <summary>
    /// Adds Dhadgar service defaults including health checks, organization context, and request logging services.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is the legacy method for non-Aspire scenarios. For Aspire integration, use
    /// <see cref="AddDhadgarServiceDefaults(IHostApplicationBuilder)"/> instead.
    /// </para>
    /// <para>
    /// This method registers:
    /// <list type="bullet">
    ///   <item>Health checks with "self" check for liveness</item>
    ///   <item><see cref="IOrganizationContext"/> for multi-tenant scenarios</item>
    ///   <item><see cref="RequestLoggingMessages"/> for source-generated HTTP logging</item>
    ///   <item>Strict JSON serialization (rejects duplicate properties, uses camelCase)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDhadgarServiceDefaults(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        services.AddOrganizationContext();
        services.AddSingleton<RequestLoggingMessages>();
        services.AddStrictJsonSerialization();

        return services;
    }

    /// <summary>
    /// Adds Dhadgar service defaults with configurable health check dependencies and OpenTelemetry instrumentation.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Configuration to read connection strings and OTLP endpoint from.</param>
    /// <param name="dependencies">Flags indicating which dependencies to check for readiness.</param>
    /// <param name="configureTracing">Optional callback for additional tracing configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is the legacy method for non-Aspire scenarios. For Aspire integration, use
    /// <see cref="AddDhadgarServiceDefaults(IHostApplicationBuilder, HealthCheckDependencies, Action{TracerProviderBuilder}?)"/> instead.
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

        services.AddOrganizationContext();
        services.AddSingleton<RequestLoggingMessages>();
        services.AddDhadgarLogging();
        services.AddStrictJsonSerialization();

        ConfigureOpenTelemetry(services, configuration, configureTracing);

        return services;
    }

    /// <summary>
    /// Configures OpenTelemetry tracing, metrics, and logging (legacy method for non-Aspire scenarios).
    /// </summary>
    private static void ConfigureOpenTelemetry(
        IServiceCollection services,
        IConfiguration configuration,
        Action<TracerProviderBuilder>? configureTracing)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        Uri? otlpUri = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsed))
        {
            otlpUri = parsed;
        }

        var serviceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
        var serviceVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion);

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();

                tracing.AddEntityFrameworkCoreInstrumentation(options =>
                {
                    options.SetDbStatementForText = true;
                    options.SetDbStatementForStoredProcedure = true;
                    options.EnrichWithIDbCommand = (activity, command) =>
                    {
                        activity.SetTag(TracingConstants.Attributes.DbSystem, TracingConstants.DatabaseSystem);
                    };
                });

                tracing.AddSource(DhadgarActivitySource.Name);
                configureTracing?.Invoke(tracing);

                if (otlpUri is not null)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddProcessInstrumentation();

                if (otlpUri is not null)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
                }
            });
    }

    #endregion

    #region Health Check Endpoints

    /// <summary>
    /// Maps Kubernetes-style health check endpoints.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <returns>The application for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Aspire provides /health and /alive endpoints. This method adds:
    /// <list type="bullet">
    ///   <item>/healthz - All health checks (for general health)</item>
    ///   <item>/livez - Only "live" tagged checks (for Kubernetes liveness probes)</item>
    ///   <item>/readyz - Only "ready" tagged checks (for Kubernetes readiness probes)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static WebApplication MapDhadgarDefaultEndpoints(this WebApplication app)
    {
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

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        var payload = new Dictionary<string, object?>
        {
            ["service"] = context.RequestServices.GetService<IHostEnvironment>()?.ApplicationName ?? "Unknown",
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

    #endregion

    #region Middleware Pipeline

    /// <summary>
    /// Registers the Dhadgar middleware pipeline in the correct order.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <param name="options">Optional middleware configuration options.</param>
    /// <returns>The web application for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers middleware in the following order:
    /// <list type="number">
    ///   <item><see cref="CorrelationMiddleware"/> - Sets CorrelationId and RequestId for distributed tracing (always enabled)</item>
    ///   <item><see cref="TenantEnrichmentMiddleware"/> - Adds TenantId, ServiceName, ServiceVersion, Hostname to logging scope (if enabled)</item>
    ///   <item><see cref="RequestLoggingMiddleware"/> - Logs HTTP requests/responses with full context (if enabled)</item>
    ///   <item><see cref="AuditMiddleware"/> - Tracks changes for auditable operations (if enabled)</item>
    /// </list>
    /// </para>
    /// <para>
    /// The order is critical:
    /// <list type="bullet">
    ///   <item><see cref="CorrelationMiddleware"/> MUST run first to establish correlation IDs</item>
    ///   <item><see cref="TenantEnrichmentMiddleware"/> MUST run second to create the logging scope with all context</item>
    ///   <item><see cref="RequestLoggingMiddleware"/> MUST run third to log within the established scope</item>
    ///   <item><see cref="AuditMiddleware"/> runs last to capture request/response for auditing</item>
    /// </list>
    /// </para>
    /// <para>
    /// Call this early in the pipeline, typically after exception handling and HTTPS redirection
    /// but before routing and authentication.
    /// </para>
    /// </remarks>
    public static WebApplication UseDhadgarMiddleware(this WebApplication app, DhadgarServiceOptions? options = null)
    {
        options ??= new DhadgarServiceOptions();

        // 1. CorrelationMiddleware - Always enabled for distributed tracing
        app.UseMiddleware<CorrelationMiddleware>();

        // 2. TenantEnrichmentMiddleware - Multi-tenant services only
        if (options.EnableTenantEnrichment)
        {
            app.UseMiddleware<TenantEnrichmentMiddleware>();
        }

        // 3. RequestLoggingMiddleware - Most services
        if (options.EnableRequestLogging)
        {
            app.UseMiddleware<RequestLoggingMiddleware>();
        }

        // 4. AuditMiddleware - Services that track changes
        if (options.EnableAuditMiddleware)
        {
            app.UseMiddleware<AuditMiddleware>();
        }

        return app;
    }

    #endregion
}
