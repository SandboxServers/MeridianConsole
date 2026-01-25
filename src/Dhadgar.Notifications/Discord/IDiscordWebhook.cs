using Dhadgar.Notifications.Alerting;

namespace Dhadgar.Notifications.Discord;

/// <summary>
/// Sends alert messages to Discord via webhook.
/// </summary>
public interface IDiscordWebhook
{
    /// <summary>
    /// Sends an alert to the configured Discord webhook.
    /// </summary>
    Task SendAlertAsync(AlertMessage alert, CancellationToken cancellationToken = default);
}
