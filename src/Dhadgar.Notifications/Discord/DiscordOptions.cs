namespace Dhadgar.Notifications.Discord;

/// <summary>
/// Configuration options for Discord webhook integration.
/// </summary>
public sealed class DiscordOptions
{
    /// <summary>Gets or sets the Discord webhook URL.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Gets or sets the bot username to display.</summary>
    public string Username { get; set; } = "Meridian Alerts";

    /// <summary>Gets or sets whether alerts are enabled.</summary>
    public bool Enabled { get; set; } = true;
}
