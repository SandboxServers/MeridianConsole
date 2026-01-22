using Dhadgar.Notifications.Discord;
using Dhadgar.Notifications.Email;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Notifications.Alerting;

/// <summary>
/// Dispatches alerts to all configured channels with throttling.
/// </summary>
public sealed class AlertDispatcher : IAlertDispatcher
{
    private readonly IDiscordWebhook _discord;
    private readonly IEmailSender _email;
    private readonly AlertThrottler _throttler;
    private readonly ILogger<AlertDispatcher> _logger;

    public AlertDispatcher(
        IDiscordWebhook discord,
        IEmailSender email,
        AlertThrottler throttler,
        ILogger<AlertDispatcher> logger)
    {
        _discord = discord;
        _email = email;
        _throttler = throttler;
        _logger = logger;
    }

    public async Task DispatchAsync(AlertMessage alert, CancellationToken cancellationToken = default)
    {
        if (!_throttler.ShouldSend(alert))
        {
            _logger.LogDebug(
                "Alert throttled: {ServiceName} - {Title}",
                alert.ServiceName,
                alert.Title);
            return;
        }

        _logger.LogInformation(
            "Dispatching alert: {ServiceName} - {Title} ({Severity})",
            alert.ServiceName,
            alert.Title,
            alert.Severity);

        // Dispatch to all channels in parallel
        var tasks = new List<Task>
        {
            _discord.SendAlertAsync(alert, cancellationToken),
            _email.SendAlertEmailAsync(alert, cancellationToken)
        };

        await Task.WhenAll(tasks);
    }
}
