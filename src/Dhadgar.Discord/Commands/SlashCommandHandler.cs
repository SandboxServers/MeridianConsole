using Dhadgar.Discord.Data;
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

            await command.RespondAsync(
                embed: BuildErrorEmbed($"Error: {ex.Message}"),
                ephemeral: true);
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
        var embed = new EmbedBuilder()
            .WithTitle("Meridian Console Status")
            .WithColor(Color.Green)
            .AddField("Discord Bot", "Online", inline: true)
            .AddField("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production", inline: true)
            .AddField("Uptime", GetUptime(), inline: true)
            .WithCurrentTimestamp()
            .WithFooter("Meridian Console")
            .Build();

        await command.RespondAsync(embed: embed);
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
        var latency = command.CreatedAt - DateTimeOffset.UtcNow;

        await command.RespondAsync(
            embed: new EmbedBuilder()
                .WithTitle("Pong!")
                .WithDescription($"Latency: {Math.Abs(latency.TotalMilliseconds):F0}ms")
                .WithColor(Color.Green)
                .Build(),
            ephemeral: true);
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
