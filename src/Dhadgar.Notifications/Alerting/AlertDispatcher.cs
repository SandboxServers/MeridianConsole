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
        ArgumentNullException.ThrowIfNull(alert);

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

        // Dispatch to all channels in parallel with error isolation
        // Each channel should not affect the others
        var discordTask = SendDiscordSafeAsync(alert, cancellationToken);
        var emailTask = SendEmailSafeAsync(alert, cancellationToken);

        await Task.WhenAll(discordTask, emailTask);
    }

    private async Task SendDiscordSafeAsync(AlertMessage alert, CancellationToken cancellationToken)
    {
        try
        {
            await _discord.SendAlertAsync(alert, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to send Discord alert: {ServiceName} - {Title}",
                alert.ServiceName,
                alert.Title);
        }
    }

    private async Task SendEmailSafeAsync(AlertMessage alert, CancellationToken cancellationToken)
    {
        try
        {
            await _email.SendAlertEmailAsync(alert, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to send email alert: {ServiceName} - {Title}",
                alert.ServiceName,
                alert.Title);
        }
    }
}
