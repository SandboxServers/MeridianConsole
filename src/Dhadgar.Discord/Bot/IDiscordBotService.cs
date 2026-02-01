using Discord;
using Discord.WebSocket;

namespace Dhadgar.Discord.Bot;

/// <summary>
/// Interface for the Discord bot service for testability.
/// </summary>
public interface IDiscordBotService
{
    /// <summary>
    /// Gets the Discord client for use by other services.
    /// </summary>
    DiscordSocketClient Client { get; }

    /// <summary>
    /// Gets the current connection state of the bot.
    /// </summary>
    ConnectionState ConnectionState { get; }
}
