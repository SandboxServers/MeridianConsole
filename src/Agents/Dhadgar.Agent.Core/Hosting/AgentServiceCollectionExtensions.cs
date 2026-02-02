using System.Net.Http;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Commands;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Hosting;

/// <summary>
/// Service collection extensions for registering agent services.
/// </summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Adds all agent core services to the service collection.
    /// <para>
    /// <strong>Platform-specific services that must be registered by the host:</strong>
    /// <list type="bullet">
    ///   <item><see cref="ICertificateStore"/> - Windows uses X509Store, Linux uses file-based storage</item>
    ///   <item><see cref="IProcessManager"/> - Windows uses Job Objects, Linux uses cgroups</item>
    /// </list>
    /// <see cref="IEnrollmentService"/> depends on <see cref="ICertificateStore"/>, so enrollment
    /// will not function until the platform-specific certificate store is registered.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAgentCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Authentication - IEnrollmentService requires ICertificateStore from platform-specific host
        services.AddSingleton<IEnrollmentService, EnrollmentService>();

        // Communication
        services.AddSingleton<IControlPlaneClient, ControlPlaneClient>();

        // Health monitoring
        services.AddSingleton<IHealthReporter, HealthReporter>();
        services.AddSingleton<ISystemMetricsCollector, SystemMetricsCollector>();
        services.AddHostedService<HeartbeatService>();

        // Commands
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<ICommandValidator, CommandValidator>();

        // Files
        services.AddSingleton<IFileTransferService, FileTransferService>();
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<IFileIntegrityChecker, FileIntegrityChecker>();

        // Telemetry
        services.AddAgentTelemetry();

        // HttpClient registrations for control plane communication
        services.AddAgentHttpClients();

        return services;
    }

    /// <summary>
    /// Registers named HttpClients for control plane communication.
    /// <para>
    /// <strong>ControlPlane</strong>: Used for initial enrollment (no mTLS, agent not yet enrolled).
    /// <strong>ControlPlaneMtls</strong>: Used for authenticated communication (mTLS with client certificate).
    /// </para>
    /// <para>
    /// SECURITY: Both clients disable automatic redirects to prevent SSRF attacks where a hostile
    /// control plane could redirect requests to internal hosts. All redirect URLs must be explicitly
    /// validated against the trusted control plane host before following.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAgentHttpClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ControlPlane: For initial enrollment (no mTLS)
        // SECURITY: Disable auto-redirects to prevent SSRF via hostile control plane
        services.AddHttpClient("ControlPlane")
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
                return new HttpClientHandler
                {
                    AllowAutoRedirect = false
                };
            })
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
                client.BaseAddress = new Uri(options.ControlPlane.Endpoint);
                client.Timeout = TimeSpan.FromSeconds(options.ControlPlane.ConnectionTimeoutSeconds);
            });

        // ControlPlaneMtls: For authenticated communication (with client certificate)
        // SECURITY: Disable auto-redirects to prevent SSRF via hostile control plane
        services.AddHttpClient("ControlPlaneMtls")
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
                var certificateStore = sp.GetRequiredService<ICertificateStore>();

                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false
                };

                // Add client certificate for mTLS if available
                var clientCert = certificateStore.GetClientCertificate();
                if (clientCert is not null)
                {
                    handler.ClientCertificates.Add(clientCert);
                }

                return handler;
            })
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
                client.BaseAddress = new Uri(options.ControlPlane.Endpoint);
                client.Timeout = TimeSpan.FromSeconds(options.ControlPlane.ConnectionTimeoutSeconds);
            });

        return services;
    }

    /// <summary>
    /// Adds OpenTelemetry for agent observability.
    /// </summary>
    public static IServiceCollection AddAgentTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AgentMeter>();
        services.TryAddSingleton<AgentActivitySource>();

        return services;
    }
}
