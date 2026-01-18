using Dhadgar.Discord.Services;
using Discord;
using Discord.WebSocket;

namespace Dhadgar.Discord.Bot;

/// <summary>
/// Hosted service that manages the Discord bot lifecycle.
/// This is for internal admin use - team notifications and commands.
/// </summary>
public sealed class DiscordBotService : IHostedService, IAsyncDisposable
{
    private readonly IDiscordCredentialProvider _credentialProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly DiscordSocketClient _client;
    private volatile bool _commandsRegistered;

    public DiscordBotService(
        IDiscordCredentialProvider credentialProvider,
        IServiceProvider serviceProvider,
        ILogger<DiscordBotService> logger)
    {
        _credentialProvider = credentialProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;

        var config = new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info,
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
            UseInteractionSnowflakeDate = false
        };

        _client = new DiscordSocketClient(config);
    }

    /// <summary>
    /// Gets the Discord client for use by other services.
    /// </summary>
    public DiscordSocketClient Client => _client;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Discord bot service...");

        _client.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.Disconnected += OnDisconnectedAsync;

        // Register slash command handler
        var commandHandler = _serviceProvider.GetRequiredService<Commands.SlashCommandHandler>();
        commandHandler.Register(_client);

        try
        {
            var token = await _credentialProvider.GetBotTokenAsync(cancellationToken);

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            _logger.LogInformation("Discord bot service started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Discord bot - check Discord:BotToken config or token from Secrets service");
            // Don't throw - allow service to start without Discord if not configured
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord bot service...");

        try
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Discord bot shutdown");
        }

        _logger.LogInformation("Discord bot service stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    private Task OnLogAsync(LogMessage message)
    {
        var logLevel = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, message.Exception, "[Discord] {Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation(
            "Discord bot ready: {Username} ({UserId}) in {GuildCount} guilds",
            _client.CurrentUser.Username,
            _client.CurrentUser.Id,
            _client.Guilds.Count);

        try
        {
            await RegisterSlashCommandsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register slash commands");
        }
    }

    private Task OnDisconnectedAsync(Exception exception)
    {
        _logger.LogWarning(exception, "Discord bot disconnected");
        return Task.CompletedTask;
    }

    private async Task RegisterSlashCommandsAsync()
    {
        // Guard against repeated registration on reconnects
        if (_commandsRegistered)
        {
            _logger.LogDebug("Slash commands already registered, skipping");
            return;
        }

        _logger.LogInformation("Registering admin slash commands...");

        var commands = new List<SlashCommandBuilder>
        {
            new SlashCommandBuilder()
                .WithName("status")
                .WithDescription("Show Meridian Console platform status"),

            new SlashCommandBuilder()
                .WithName("logs")
                .WithDescription("Show recent notification logs"),

            new SlashCommandBuilder()
                .WithName("ping")
                .WithDescription("Check bot latency")
        };

        try
        {
            // Use bulk registration for efficiency (single API call)
            var builtCommands = commands.Select(c => c.Build()).ToArray();
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(builtCommands);
            _commandsRegistered = true;

            _logger.LogInformation("Registered {Count} admin slash commands", builtCommands.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register slash commands");
        }
    }
}
