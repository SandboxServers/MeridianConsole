using System.Text;
using System.Text.Json;
using Dhadgar.Notifications.Alerting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Notifications.Discord;

/// <summary>
/// Sends alerts to Discord via webhook API.
/// </summary>
public sealed class DiscordWebhookClient : IDiscordWebhook
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordWebhookClient> _logger;

    public DiscordWebhookClient(
        HttpClient httpClient,
        IOptions<DiscordOptions> options,
        ILogger<DiscordWebhookClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAlertAsync(AlertMessage alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (!_options.Enabled)
        {
            _logger.LogDebug("Discord alerting is disabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            _logger.LogDebug("Discord webhook URL is not configured");
            return;
        }

        var color = alert.Severity switch
        {
            AlertSeverity.Critical => 0xFF0000, // Red
            AlertSeverity.Error => 0xFFA500,    // Orange
            AlertSeverity.Warning => 0xFFFF00,  // Yellow
            _ => 0x808080                       // Gray
        };

        var fields = new List<object>
        {
            new { name = "Service", value = alert.ServiceName, inline = true },
            new { name = "Severity", value = alert.Severity.ToString(), inline = true }
        };

        if (!string.IsNullOrWhiteSpace(alert.TraceId))
        {
            fields.Add(new { name = "TraceId", value = $"`{alert.TraceId}`", inline = false });
        }

        if (!string.IsNullOrWhiteSpace(alert.CorrelationId))
        {
            fields.Add(new { name = "CorrelationId", value = $"`{alert.CorrelationId}`", inline = false });
        }

        if (!string.IsNullOrWhiteSpace(alert.ExceptionType))
        {
            fields.Add(new { name = "Exception", value = alert.ExceptionType, inline = true });
        }

        var payload = new
        {
            username = _options.Username,
            embeds = new[]
            {
                new
                {
                    title = alert.Title,
                    description = TruncateMessage(alert.Message, 2048),
                    color,
                    timestamp = alert.Timestamp.ToString("O"),
                    fields,
                    footer = new { text = $"Meridian Console | {alert.Timestamp:yyyy-MM-dd HH:mm:ss} UTC" }
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(_options.WebhookUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Discord webhook returned {StatusCode}: {Response}",
                response.StatusCode,
                responseBody);
        }
    }

    private static string TruncateMessage(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message) || message.Length <= maxLength)
            return message;

        return string.Concat(message.AsSpan(0, maxLength - 3), "...");
    }
}
