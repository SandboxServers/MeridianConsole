using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Dhadgar.ServiceDefaults.Tracing;

/// <summary>
/// Extension methods for configuring Dhadgar distributed tracing infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// These extensions provide a centralized way to configure OpenTelemetry tracing across all Dhadgar services,
/// including automatic instrumentation for ASP.NET Core, HTTP clients, and Entity Framework Core.
/// </para>
/// <para>
/// Typical usage in Program.cs:
/// </para>
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
///
/// // Add tracing with EF Core instrumentation
/// builder.Services.AddDhadgarTracing(builder.Configuration, "Dhadgar.Servers");
///
/// // Or with custom configuration for Redis
/// builder.Services.AddDhadgarTracing(builder.Configuration, "Dhadgar.Servers", tracing =>
/// {
///     tracing.AddRedisInstrumentation(connection);
///     tracing.AddSource("Dhadgar.CustomComponent");
/// });
/// </code>
/// </remarks>
public static class TracingExtensions
{
    /// <summary>
    /// Adds Dhadgar distributed tracing with ASP.NET Core, HTTP client, and EF Core instrumentation.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="serviceName">The name of the service (e.g., "Dhadgar.Servers").</param>
    /// <param name="configureTracing">Optional callback for additional tracing configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>This method configures:</para>
    /// <list type="bullet">
    ///   <item>ASP.NET Core instrumentation - HTTP request/response spans</item>
    ///   <item>HTTP client instrumentation - outbound HTTP call spans</item>
    ///   <item>Entity Framework Core instrumentation - database query spans with db.system tag</item>
    ///   <item>OTLP exporter if OpenTelemetry:OtlpEndpoint is configured</item>
    /// </list>
    /// <para>
    /// Configuration keys:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>OpenTelemetry:OtlpEndpoint</c> - OTLP collector endpoint (e.g., "http://localhost:4317")</item>
    /// </list>
    /// <para>
    /// For services using Redis, call AddRedisInstrumentation in the configureTracing callback:
    /// </para>
    /// <code>
    /// builder.Services.AddDhadgarTracing(config, "Dhadgar.Servers", tracing =>
    /// {
    ///     var connection = ConnectionMultiplexer.Connect("localhost:6379");
    ///     tracing.AddRedisInstrumentation(connection);
    /// });
    /// </code>
    /// <para>
    /// For custom activity sources (custom spans), register them via AddSource:
    /// </para>
    /// <code>
    /// builder.Services.AddDhadgarTracing(config, "Dhadgar.Servers", tracing =>
    /// {
    ///     tracing.AddSource("Dhadgar.TaskScheduler");
    /// });
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Basic usage with just EF Core
    /// builder.Services.AddDhadgarTracing(builder.Configuration, "Dhadgar.Servers");
    ///
    /// // With Redis instrumentation
    /// builder.Services.AddDhadgarTracing(builder.Configuration, "Dhadgar.Servers", tracing =>
    /// {
    ///     var redis = ConnectionMultiplexer.Connect(connectionString);
    ///     builder.Services.AddSingleton&lt;IConnectionMultiplexer&gt;(redis);
    ///     tracing.AddRedisInstrumentation(redis);
    /// });
    ///
    /// // With custom activity source
    /// builder.Services.AddDhadgarTracing(builder.Configuration, "Dhadgar.Tasks", tracing =>
    /// {
    ///     tracing.AddSource("Dhadgar.TaskScheduler");
    ///     tracing.AddSource("Dhadgar.TaskExecutor");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddDhadgarTracing(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        // Get OTLP endpoint from configuration
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        Uri? otlpUri = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsed))
        {
            otlpUri = parsed;
        }

        // Build resource with service name and version
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: serviceName,
                serviceVersion: typeof(TracingExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0");

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
                    tracing.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = otlpUri;
                    });
                }
            });

        return services;
    }

    /// <summary>
    /// Adds Dhadgar distributed tracing, using entry assembly name as service name.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configureTracing">Optional callback for additional tracing configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This overload automatically determines the service name from the entry assembly.
    /// Use the explicit service name overload when the assembly name doesn't match the desired service name.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDhadgarTracing(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        var serviceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
        return services.AddDhadgarTracing(configuration, serviceName, configureTracing);
    }
}
