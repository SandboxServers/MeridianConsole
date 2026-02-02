using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.GameServerWrapper;

/// <summary>
/// Entry point for the GameServerWrapper service.
/// </summary>
public static class Program
{
    /// <summary>
    /// Event log source name.
    /// </summary>
    public const string EventLogSourceName = "Meridian GameServer Wrapper";

    public static async Task<int> Main(string[] args)
    {
        // Parse command-line arguments
        var (options, errors) = WrapperOptions.Parse(args);

        if (options is null || errors.Count > 0)
        {
            Console.Error.WriteLine("GameServerWrapper: Invalid arguments");
            foreach (var error in errors)
            {
                Console.Error.WriteLine($"  {error}");
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage: GameServerWrapper.exe --server-id=<id> --pipe=<pipename> --config=<path>");
            return 1;
        }

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configure as Windows Service - dynamic name based on server ID
            var serviceName = $"MeridianGS_{options.ServerId}";
            builder.Services.AddWindowsService(serviceOptions =>
            {
                serviceOptions.ServiceName = serviceName;
            });

            // Register options
            builder.Services.AddSingleton(options);

            // Register time provider for testability
            builder.Services.AddSingleton(TimeProvider.System);

            // Register the hosted service
            builder.Services.AddHostedService<GameServerHostedService>();

            // Add Windows Event Log logging
            builder.Logging.AddEventLog(settings =>
            {
                settings.SourceName = EventLogSourceName;
                settings.LogName = "Application";
            });

            var host = builder.Build();

            // Log startup
            var logger = host.Services.GetRequiredService<ILogger<GameServerHostedService>>();
            logger.LogInformation(
                "GameServerWrapper starting. ServerId: {ServerId}, Pipe: {Pipe}",
                options.ServerId,
                options.PipeName);

            await host.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}
