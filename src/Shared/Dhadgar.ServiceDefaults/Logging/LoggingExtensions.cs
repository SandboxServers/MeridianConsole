using Dhadgar.ServiceDefaults.Logging.Redactors;
using Dhadgar.ServiceDefaults.Security;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Dhadgar.ServiceDefaults.Logging;

/// <summary>
/// Extension methods for configuring Dhadgar logging infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// These extensions provide a centralized way to configure logging across all Dhadgar services,
/// including PII redaction, OpenTelemetry integration, and security event logging.
/// </para>
/// <para>
/// Typical usage in Program.cs:
/// </para>
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
///
/// // Add redaction and security logging services
/// builder.Services.AddDhadgarLogging();
///
/// // Configure OpenTelemetry logging with redaction
/// builder.Logging.AddDhadgarLogging("Dhadgar.Servers", builder.Configuration);
/// </code>
/// </remarks>
public static class LoggingExtensions
{
    /// <summary>
    /// Adds Dhadgar logging services including redaction and security event logging.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>This method registers:</para>
    /// <list type="bullet">
    ///   <item>Redaction services with custom redactors for Email, Token, Password, ConnectionString, and ApiKey</item>
    ///   <item><see cref="ISecurityEventLogger"/> for structured security event logging</item>
    /// </list>
    /// <para>
    /// Redactor mappings:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="DhadgarDataClassifications.Email"/> -> <see cref="EmailRedactor"/> (outputs "***@***.***")</item>
    ///   <item><see cref="DhadgarDataClassifications.Token"/> -> <see cref="TokenRedactor"/> (outputs "[REDACTED-TOKEN:len=N]")</item>
    ///   <item><see cref="DhadgarDataClassifications.ApiKey"/> -> <see cref="TokenRedactor"/> (same as Token)</item>
    ///   <item><see cref="DhadgarDataClassifications.Password"/> -> <see cref="ErasingRedactor"/> (complete erasure)</item>
    ///   <item><see cref="DhadgarDataClassifications.ConnectionString"/> -> <see cref="ConnectionStringRedactor"/> (preserves Host/Database)</item>
    ///   <item><see cref="DhadgarDataClassifications.IpAddress"/> -> <see cref="ErasingRedactor"/> (complete erasure when redaction needed)</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddDhadgarLogging(this IServiceCollection services)
    {
        // Register redaction services with custom redactors
        services.AddRedaction(builder =>
        {
            builder.SetRedactor<EmailRedactor>(DhadgarDataClassifications.Email);
            builder.SetRedactor<TokenRedactor>(DhadgarDataClassifications.Token);
            builder.SetRedactor<TokenRedactor>(DhadgarDataClassifications.ApiKey);
            builder.SetRedactor<ErasingRedactor>(DhadgarDataClassifications.Password);
            builder.SetRedactor<ConnectionStringRedactor>(DhadgarDataClassifications.ConnectionString);
            builder.SetRedactor<ErasingRedactor>(DhadgarDataClassifications.IpAddress);
        });

        // Register security event logger
        services.AddSingleton<ISecurityEventLogger, SecurityEventLogger>();

        return services;
    }

    /// <summary>
    /// Configures OpenTelemetry logging with redaction support for Dhadgar services.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <param name="serviceName">The name of the service (e.g., "Dhadgar.Servers").</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The logging builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures OpenTelemetry logging with:
    /// </para>
    /// <list type="bullet">
    ///   <item>Redaction enabled in the logging pipeline (requires Microsoft.Extensions.Telemetry)</item>
    ///   <item>Service name and version in resource attributes</item>
    ///   <item>Formatted messages included for readability</item>
    ///   <item>Scopes included - CRITICAL for correlation IDs and tenant context</item>
    ///   <item>State values parsed for structured logging</item>
    ///   <item>OTLP exporter if OpenTelemetry:OtlpEndpoint is configured</item>
    /// </list>
    /// <para>
    /// Configuration keys:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>OpenTelemetry:OtlpEndpoint</c> - OTLP collector endpoint (e.g., "http://localhost:4317")</item>
    /// </list>
    /// <para>
    /// IMPORTANT: <c>IncludeScopes = true</c> is required for correlation IDs and tenant context
    /// to appear in logs. Without this, context added via <c>logger.BeginScope()</c> is lost.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // In Program.cs
    /// builder.Logging.AddDhadgarLogging("Dhadgar.Servers", builder.Configuration);
    ///
    /// // With user secrets for local development:
    /// // dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317"
    /// </code>
    /// </example>
    public static ILoggingBuilder AddDhadgarLogging(
        this ILoggingBuilder builder,
        string serviceName,
        IConfiguration configuration)
    {
        // Enable redaction in the logging pipeline
        // This requires Microsoft.Extensions.Telemetry package
        builder.EnableRedaction();

        // Get OTLP endpoint from configuration
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        Uri? otlpUri = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsed))
        {
            otlpUri = parsed;
        }

        // Configure OpenTelemetry logging
        builder.AddOpenTelemetry(options =>
        {
            // Set service name and version in resource attributes
            options.SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: serviceName,
                        serviceVersion: typeof(LoggingExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0"));

            // Include formatted message for human readability in log viewers
            options.IncludeFormattedMessage = true;

            // CRITICAL: Include scopes for correlation ID and tenant context
            // Without this, BeginScope() context is lost and logs cannot be correlated
            options.IncludeScopes = true;

            // Parse state values for structured logging properties
            options.ParseStateValues = true;

            // Add OTLP exporter if endpoint is configured
            if (otlpUri is not null)
            {
                options.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = otlpUri;
                });
            }
        });

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry logging with redaction support, using environment-based service name.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The logging builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This overload automatically determines the service name from the entry assembly.
    /// Use the explicit service name overload when the assembly name doesn't match the desired service name.
    /// </para>
    /// </remarks>
    public static ILoggingBuilder AddDhadgarLogging(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        var serviceName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
        return builder.AddDhadgarLogging(serviceName, configuration);
    }
}
