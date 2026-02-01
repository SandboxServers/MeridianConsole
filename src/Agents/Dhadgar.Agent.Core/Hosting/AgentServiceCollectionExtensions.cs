using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Commands;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
