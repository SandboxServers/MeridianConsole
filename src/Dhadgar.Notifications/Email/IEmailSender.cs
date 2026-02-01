using Dhadgar.Notifications.Alerting;

namespace Dhadgar.Notifications.Email;

/// <summary>
/// Sends alert emails via SMTP.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an alert email to configured recipients.
    /// </summary>
    Task SendAlertEmailAsync(AlertMessage alert, CancellationToken cancellationToken = default);
}
