using System.Globalization;
using System.Text;
using Dhadgar.Notifications.Alerting;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Dhadgar.Notifications.Email;

/// <summary>
/// Sends alert emails using MailKit SMTP client.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAlertEmailAsync(AlertMessage alert, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.AlertRecipients))
        {
            _logger.LogDebug("Email alerting disabled or no recipients configured");
            return;
        }

        var severityLabel = alert.Severity.ToString().ToUpperInvariant();
        var subject = $"[{severityLabel}] {alert.ServiceName}: {alert.Title}";

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.SenderName, _options.SenderEmail));

        foreach (var recipient in _options.AlertRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            message.To.Add(new MailboxAddress(null, recipient));
        }

        message.Subject = subject;

        var htmlBody = BuildHtmlBody(alert);
        message.Body = new TextPart("html") { Text = htmlBody };

        try
        {
            using var client = new SmtpClient();

            var secureSocketOptions = _options.UseTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(
                _options.SmtpHost,
                _options.SmtpPort,
                secureSocketOptions,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
            {
                await client.AuthenticateAsync(
                    _options.SmtpUsername,
                    _options.SmtpPassword,
                    cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Alert email sent: {Subject}", subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert email: {Subject}", subject);
        }
    }

    private static string BuildHtmlBody(AlertMessage alert)
    {
        var severityColor = alert.Severity switch
        {
            AlertSeverity.Critical => "#dc3545",
            AlertSeverity.Error => "#fd7e14",
            AlertSeverity.Warning => "#ffc107",
            _ => "#6c757d"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"utf-8\">");
        sb.AppendLine("    <style>");
        sb.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px; background: #f5f5f5; }");
        sb.AppendLine("        .container { max-width: 600px; margin: 0 auto; background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
        sb.AppendLine(CultureInfo.InvariantCulture, $"        .header {{ background: {severityColor}; color: white; padding: 20px; }}");
        sb.AppendLine("        .header h1 { margin: 0; font-size: 24px; }");
        sb.AppendLine("        .content { padding: 20px; }");
        sb.AppendLine("        .message { background: #f8f9fa; padding: 15px; border-radius: 4px; margin-bottom: 20px; white-space: pre-wrap; }");
        sb.AppendLine("        table { width: 100%; border-collapse: collapse; }");
        sb.AppendLine("        th, td { padding: 8px; text-align: left; border: 1px solid #ddd; }");
        sb.AppendLine("        th { background: #f8f9fa; }");
        sb.AppendLine("        .footer { padding: 15px 20px; background: #f8f9fa; font-size: 12px; color: #666; }");
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <div class=\"container\">");
        sb.AppendLine("        <div class=\"header\">");
        sb.AppendLine(CultureInfo.InvariantCulture, $"            <h1>{System.Net.WebUtility.HtmlEncode(alert.Title)}</h1>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"content\">");
        sb.AppendLine(CultureInfo.InvariantCulture, $"            <div class=\"message\">{System.Net.WebUtility.HtmlEncode(alert.Message)}</div>");
        sb.AppendLine("            <table>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                <tr><td style=\"padding: 8px; border: 1px solid #ddd;\"><strong>Service</strong></td><td style=\"padding: 8px; border: 1px solid #ddd;\">{System.Net.WebUtility.HtmlEncode(alert.ServiceName)}</td></tr>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                <tr><td style=\"padding: 8px; border: 1px solid #ddd;\"><strong>Severity</strong></td><td style=\"padding: 8px; border: 1px solid #ddd;\">{alert.Severity}</td></tr>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                <tr><td style=\"padding: 8px; border: 1px solid #ddd;\"><strong>Timestamp</strong></td><td style=\"padding: 8px; border: 1px solid #ddd;\">{alert.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</td></tr>");

        if (!string.IsNullOrWhiteSpace(alert.TraceId))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"                <tr><td style=\"padding: 8px; border: 1px solid #ddd;\"><strong>Trace ID</strong></td><td style=\"padding: 8px; border: 1px solid #ddd;\"><code>{System.Net.WebUtility.HtmlEncode(alert.TraceId)}</code></td></tr>");
        }

        if (!string.IsNullOrWhiteSpace(alert.CorrelationId))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"                <tr><td style=\"padding: 8px; border: 1px solid #ddd;\"><strong>Correlation ID</strong></td><td style=\"padding: 8px; border: 1px solid #ddd;\"><code>{System.Net.WebUtility.HtmlEncode(alert.CorrelationId)}</code></td></tr>");
        }

        if (!string.IsNullOrWhiteSpace(alert.ExceptionType))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"                <tr><td style=\"padding: 8px; border: 1px solid #ddd;\"><strong>Exception</strong></td><td style=\"padding: 8px; border: 1px solid #ddd;\">{System.Net.WebUtility.HtmlEncode(alert.ExceptionType)}</td></tr>");
        }

        if (alert.AdditionalData is not null)
        {
            foreach (var kvp in alert.AdditionalData)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"                <tr><td style=\"padding: 8px; border: 1px solid #ddd;\"><strong>{System.Net.WebUtility.HtmlEncode(kvp.Key)}</strong></td><td style=\"padding: 8px; border: 1px solid #ddd;\">{System.Net.WebUtility.HtmlEncode(kvp.Value)}</td></tr>");
            }
        }

        sb.AppendLine("            </table>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"footer\">");
        sb.AppendLine("            Meridian Console Alert System");
        sb.AppendLine("        </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
