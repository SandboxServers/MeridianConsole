using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Agent.Core.Process;
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

            // Add Windows Firewall manager
            builder.Services.AddSingleton<FirewallManager>();

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
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}
