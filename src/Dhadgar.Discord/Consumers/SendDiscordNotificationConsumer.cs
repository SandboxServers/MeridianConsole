using System.Text.Json;
using Dhadgar.Contracts.Notifications;
using Dhadgar.Discord.Data;
using MassTransit;

namespace Dhadgar.Discord.Consumers;

/// <summary>
/// Consumes SendDiscordNotification commands and posts to the admin Discord webhook.
/// This is for internal team notifications - not customer-facing.
/// </summary>
public sealed class SendDiscordNotificationConsumer : IConsumer<SendDiscordNotification>
{
    private readonly DiscordDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendDiscordNotificationConsumer> _logger;

    public SendDiscordNotificationConsumer(
        DiscordDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SendDiscordNotificationConsumer> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendDiscordNotification> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing notification for event {EventType}: {Title}",
            message.EventType, message.Title);

        // Get the admin webhook URL from config
        var webhookUrl = _configuration["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _logger.LogWarning("Discord:WebhookUrl not configured, skipping notification");
            return;
        }

        // Determine which channel based on event type
        var channel = GetChannelForEvent(message.EventType, message.Severity);

        // Build the Discord embed
        var embed = BuildEmbed(message);
        var payload = new { embeds = new[] { embed } };
        var jsonPayload = JsonSerializer.Serialize(payload);

        var httpClient = _httpClientFactory.CreateClient();

        try
        {
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(webhookUrl, content, context.CancellationToken);

            var logEntry = new DiscordNotificationLog
            {
                Id = Guid.NewGuid(),
                EventType = Truncate(message.EventType, 100),
                Channel = Truncate(channel, 100),
                Title = Truncate(message.Title, 500),
                Status = response.IsSuccessStatusCode ? "sent" : "failed",
                ErrorMessage = response.IsSuccessStatusCode ? null : Truncate($"HTTP {response.StatusCode}", 1000),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _db.NotificationLogs.Add(logEntry);
            await _db.SaveChangesAsync(context.CancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully sent notification to Discord");
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(context.CancellationToken);
                _logger.LogWarning(
                    "Failed to send notification to Discord: {StatusCode} - {Error}",
                    response.StatusCode, errorBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending notification to Discord");

            _db.NotificationLogs.Add(new DiscordNotificationLog
            {
                Id = Guid.NewGuid(),
                EventType = Truncate(message.EventType, 100),
                Channel = Truncate(channel, 100),
                Title = Truncate(message.Title, 500),
                Status = "failed",
                ErrorMessage = Truncate(ex.Message, 1000),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(context.CancellationToken);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string GetChannelForEvent(string eventType, string severity)
    {
        // Could route to different channels based on severity/type
        // For now, just categorize for logging
        return severity switch
        {
            NotificationSeverity.Error or NotificationSeverity.Critical => "alerts",
            NotificationSeverity.Warning => "warnings",
            _ => "general"
        };
    }

    private static object BuildEmbed(SendDiscordNotification message)
    {
        var color = message.Severity switch
        {
            NotificationSeverity.Error => 0xFF0000,    // Red
            NotificationSeverity.Critical => 0xFF0000, // Red
            NotificationSeverity.Warning => 0xFFAA00,  // Orange
            _ => 0x5865F2                              // Discord blurple
        };

        var embed = new Dictionary<string, object>
        {
            ["title"] = message.Title,
            ["description"] = message.Message,
            ["color"] = color,
            ["timestamp"] = message.OccurredAtUtc.ToString("o"),
            ["footer"] = new Dictionary<string, string>
            {
                ["text"] = $"Meridian Console • {message.EventType}"
            }
        };

        // Add fields if present
        if (message.Fields is { Count: > 0 })
        {
            var fields = message.Fields.Select(kvp => new Dictionary<string, object>
            {
                ["name"] = kvp.Key,
                ["value"] = kvp.Value,
                ["inline"] = true
            }).ToList();

            embed["fields"] = fields;
        }

        return embed;
    }
}
