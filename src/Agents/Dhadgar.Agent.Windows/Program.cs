using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Agent.Windows.Installation;
using Dhadgar.Agent.Windows.IPC;
using Dhadgar.Agent.Windows.Security;
using Dhadgar.Agent.Windows.Services;
using Dhadgar.Agent.Windows.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Windows;

/// <summary>
/// Entry point for the Windows Agent service.
/// </summary>
public static class Program
{
    /// <summary>
    /// Service name used for Windows Service registration.
    /// </summary>
    public const string ServiceName = "DhadgarAgent";

    /// <summary>
    /// Event log source name.
    /// </summary>
    public const string EventLogSourceName = "Meridian Console Agent";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configure as Windows Service - MUST be called early
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = ServiceName;
            });

            // Configure Agent options with validation
            builder.Services
                .AddOptions<AgentOptions>()
                .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Add Agent.Core services (requires ICertificateStore and IProcessManager)
            builder.Services.AddAgentCore();

            // Add Windows-specific implementations (required by Agent.Core)
            builder.Services.AddSingleton<ICertificateStore, WindowsCertificateStore>();
            builder.Services.AddSingleton<IProcessManager, WindowsProcessManager>();
            builder.Services.AddSingleton<IEnrollmentTokenCleanup, EnrollmentTokenCleanup>();

            // Add Windows Firewall manager
            builder.Services.AddSingleton<FirewallManager>();

            // Add service isolation components (used when Process.UseServiceIsolation is true)
            builder.Services.AddSingleton<IWindowsServiceManager, WindowsServiceManager>();
            builder.Services.AddSingleton<IDirectoryAclManager, DirectoryAclManager>();

            // Register AgentPipeServer factory - requires NodeId which may not be known until enrollment
            builder.Services.AddSingleton<IAgentPipeServer>(sp =>
            {
                var agentOptions = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<AgentPipeServer>>();
                var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;

                // Use NodeId if enrolled, otherwise use a placeholder GUID
                // (pipe server won't be started until after enrollment anyway)
                var nodeId = agentOptions.NodeId ?? Guid.Empty;

                return new AgentPipeServer(nodeId, logger, timeProvider);
            });

            // Add Windows Event Log logging
            builder.Logging.AddEventLog(settings =>
            {
                settings.SourceName = EventLogSourceName;
                settings.LogName = "Application";
            });

            var host = builder.Build();

            // Perform startup validation
            using (var scope = host.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<AgentOptions>>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<AgentOptions>>().Value;

                // SECURITY: Validate HTTPS enforcement at startup
                if (!Uri.TryCreate(options.ControlPlane.Endpoint, UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogCritical(
                        "Control plane endpoint must use HTTPS. Current value: {Scheme}",
                        uri?.Scheme ?? "invalid");
                    Console.Error.WriteLine("FATAL: Control plane endpoint must use HTTPS");
                    return 1;
                }

                logger.LogInformation(
                    "Windows Agent starting. NodeId: {NodeId}, ControlPlane: {Endpoint}",
                    options.NodeId?.ToString() ?? "(not enrolled)",
                    uri.Host);

                // Configure service recovery with progressive delays (5s/10s/30s).
                // This overrides WiX's uniform delay to set proper progression via sc.exe.
                // Runs on every startup to ensure correct config even after service reinstall.
                var recoveryResult = await ServiceInstaller.ConfigureRecoveryAsync();
                if (recoveryResult.IsFailure)
                {
                    // Log warning but don't fail startup - service will still work with WiX baseline delays
                    logger.LogWarning(
                        "Failed to configure service recovery delays: {Error}. " +
                        "Service will use uniform 5-second delays instead of 5s/10s/30s progression.",
                        recoveryResult.Error);
                }
                else
                {
                    logger.LogDebug("Service recovery configured with progressive delays (5s/10s/30s)");
                }
            }

            await host.RunAsync();
            return 0;
        }
        catch (OptionsValidationException ex)
        {
            Console.Error.WriteLine($"Configuration validation failed: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            // Output full exception for diagnostics, not just Message
            Console.Error.WriteLine($"Fatal error: {ex}");
            return 1;
        }
    }
}
