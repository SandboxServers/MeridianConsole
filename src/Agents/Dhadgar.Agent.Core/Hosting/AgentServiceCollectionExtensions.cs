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
    /// Platform-specific implementations (ICertificateStore, IProcessManager)
    /// must be registered separately by the Windows or Linux agent.
    /// </summary>
    public static IServiceCollection AddAgentCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
