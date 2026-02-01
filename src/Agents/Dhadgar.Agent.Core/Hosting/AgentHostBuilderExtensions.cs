using Dhadgar.Agent.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AgentFileOptions = Dhadgar.Agent.Core.Configuration.FileOptions;

namespace Dhadgar.Agent.Core.Hosting;

/// <summary>
/// Extensions for configuring agent host defaults.
/// </summary>
public static class AgentHostBuilderExtensions
{
    /// <summary>
    /// Configures the host builder with agent defaults including configuration,
    /// logging, and core services.
    /// </summary>
    public static IHostBuilder ConfigureAgentDefaults(this IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .ConfigureAppConfiguration(ConfigureConfiguration)
            .ConfigureServices(ConfigureServices);
    }

    private static void ConfigureConfiguration(HostBuilderContext context, IConfigurationBuilder config)
    {
        var env = context.HostingEnvironment;

        // Clear default sources and add our specific order
        config.Sources.Clear();

        config
            // Base configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
            // Platform-specific configuration (set by Windows/Linux agents)
            .AddJsonFile("appsettings.platform.json", optional: true, reloadOnChange: true)
            // Environment variables with DHADGAR_ prefix
            .AddEnvironmentVariables("DHADGAR_")
            // Command line overrides (skip first arg which is the executable path)
            .AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // Bind and validate configuration
        services.AddOptions<AgentOptions>()
            .Bind(context.Configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Make IOptions<T> available for nested options
        services.AddOptions<ControlPlaneOptions>()
            .Bind(context.Configuration.GetSection($"{AgentOptions.SectionName}:ControlPlane"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(context.Configuration.GetSection($"{AgentOptions.SectionName}:Security"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ProcessOptions>()
            .Bind(context.Configuration.GetSection($"{AgentOptions.SectionName}:Process"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AgentFileOptions>()
            .Bind(context.Configuration.GetSection($"{AgentOptions.SectionName}:Files"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
