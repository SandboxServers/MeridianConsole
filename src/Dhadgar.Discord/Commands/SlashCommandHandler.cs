using Dhadgar.Discord.Data;
using Dhadgar.Discord.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Discord.Commands;

/// <summary>
/// Handles slash commands for internal admin use.
/// These are commands for the Meridian team to interact with the platform via Discord.
/// </summary>
public sealed class SlashCommandHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SlashCommandHandler> _logger;

    public SlashCommandHandler(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<SlashCommandHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Registers this handler with the Discord client.
    /// </summary>
    public void Register(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += HandleSlashCommandAsync;
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        _logger.LogInformation(
            "Admin command: /{Command} from {User} ({UserId})",
            command.Data.Name,
            command.User.Username,
            command.User.Id);

        // Check if user is an admin (configured Discord user IDs)
        if (!IsAdmin(command.User.Id))
        {
            await command.RespondAsync(
                embed: BuildErrorEmbed("You are not authorized to use admin commands."),
                ephemeral: true);
            return;
        }

        try
        {
            switch (command.Data.Name)
            {
                case "status":
                    await HandleStatusCommandAsync(command);
                    break;
                case "logs":
                    await HandleLogsCommandAsync(command);
                    break;
                case "ping":
                    await HandlePingCommandAsync(command);
                    break;
                default:
                    await command.RespondAsync(
                        embed: BuildErrorEmbed($"Unknown command: {command.Data.Name}"),
                        ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling admin command /{Command}", command.Data.Name);

            // Don't leak exception details to Discord - log internally only
            var errorEmbed = BuildErrorEmbed("An internal error occurred. Check logs for details.");

            // Use FollowupAsync if interaction was deferred, RespondAsync otherwise
            if (command.HasResponded)
            {
                await command.FollowupAsync(embed: errorEmbed, ephemeral: true);
            }
            else
            {
                await command.RespondAsync(embed: errorEmbed, ephemeral: true);
            }
        }
    }

    private bool IsAdmin(ulong discordUserId)
    {
        // Get allowed admin user IDs from config
        var adminIds = _configuration.GetSection("Discord:AdminUserIds").Get<ulong[]>();
        if (adminIds is null || adminIds.Length == 0)
        {
            // If no admins configured, allow no one (fail closed)
            _logger.LogWarning("No Discord:AdminUserIds configured - all admin commands blocked");
            return false;
        }

        return adminIds.Contains(discordUserId);
    }

    private async Task HandleStatusCommandAsync(SocketSlashCommand command)
    {
        // Defer response since health checks may take a moment
        await command.DeferAsync();

        using var scope = _serviceProvider.CreateScope();
        var healthService = scope.ServiceProvider.GetRequiredService<IPlatformHealthService>();

        var healthStatus = await healthService.CheckAllServicesAsync();

        // Build status embed with service health
        var embedBuilder = new EmbedBuilder()
            .WithTitle("Meridian Console Platform Status")
            .WithCurrentTimestamp()
            .WithFooter("Meridian Console");

        // Overall status color
        if (healthStatus.unhealthyCount == 0)
        {
            embedBuilder.WithColor(Color.Green);
            embedBuilder.WithDescription($"All {healthStatus.healthyCount} services operational");
        }
        else if (healthStatus.healthyCount == 0)
        {
            embedBuilder.WithColor(Color.Red);
            embedBuilder.WithDescription("All services are down!");
        }
        else
        {
            embedBuilder.WithColor(Color.Orange);
            embedBuilder.WithDescription($"{healthStatus.healthyCount}/{healthStatus.services.Count} services operational");
        }

        // Group services by status for cleaner display
        var healthyServices = healthStatus.services.Where(s => s.isHealthy).ToList();
        var unhealthyServices = healthStatus.services.Where(s => !s.isHealthy).ToList();

        // Show healthy services
        if (healthyServices.Count > 0)
        {
            var healthyText = string.Join("\n", healthyServices.Select(s =>
                $":green_circle: **{s.serviceName}** ({s.responseTimeMs}ms)"));
            embedBuilder.AddField("Healthy", healthyText, inline: false);
        }

        // Show unhealthy services
        if (unhealthyServices.Count > 0)
        {
            var unhealthyText = string.Join("\n", unhealthyServices.Select(s =>
                $":red_circle: **{s.serviceName}** - {s.error ?? "Unknown error"}"));
            embedBuilder.AddField("Unhealthy", unhealthyText, inline: false);
        }

        // Add system info
        embedBuilder.AddField("Environment",
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            inline: true);
        embedBuilder.AddField("Bot Uptime", GetUptime(), inline: true);

        await command.FollowupAsync(embed: embedBuilder.Build());
    }

    private async Task HandleLogsCommandAsync(SocketSlashCommand command)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

        var recentLogs = await db.NotificationLogs
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        if (recentLogs.Count == 0)
        {
            await command.RespondAsync(
                embed: BuildInfoEmbed("No Logs", "No notification logs found."),
                ephemeral: true);
            return;
        }

        var logText = string.Join("\n", recentLogs.Select(l =>
            $"`{l.CreatedAtUtc:HH:mm:ss}` [{l.Status}] {l.EventType}: {l.Title[..Math.Min(50, l.Title.Length)]}"));

        var embed = new EmbedBuilder()
            .WithTitle("Recent Notification Logs")
            .WithDescription(logText)
            .WithColor(Color.Blue)
            .WithCurrentTimestamp()
            .Build();

        await command.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandlePingCommandAsync(SocketSlashCommand command)
    {
        // Measure round-trip time by responding and modifying
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await command.RespondAsync(
            embed: new EmbedBuilder().WithDescription("Pinging...").Build(),
            ephemeral: true);
        stopwatch.Stop();

        var rtt = stopwatch.ElapsedMilliseconds;

        await command.ModifyOriginalResponseAsync(props =>
            props.Embed = new EmbedBuilder()
                .WithTitle("Pong!")
                .WithDescription($"Round-trip: **{rtt}ms**")
                .WithColor(Color.Green)
                .Build());
    }

    private static string GetUptime()
    {
        var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        if (uptime.TotalDays >= 1)
            return $"{uptime.Days}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }

    private static Embed BuildInfoEmbed(string title, string description) =>
        new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(Color.Blue)
            .WithCurrentTimestamp()
            .Build();

    private static Embed BuildErrorEmbed(string message) =>
        new EmbedBuilder()
            .WithTitle("Error")
            .WithDescription(message)
            .WithColor(Color.Red)
            .WithCurrentTimestamp()
            .Build();
}
