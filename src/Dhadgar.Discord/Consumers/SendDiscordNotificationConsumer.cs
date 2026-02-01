using System.Net;
using System.Text.Json;
using Dhadgar.Contracts.Notifications;
using Dhadgar.Discord.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dhadgar.Discord.Consumers;

/// <summary>
/// Consumes SendDiscordNotification commands and posts to the admin Discord webhook.
/// This is for internal team notifications - not customer-facing.
/// </summary>
public sealed class SendDiscordNotificationConsumer : IConsumer<SendDiscordNotification>
{
    private const int MaxRetryAttempts = 3;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 30000;

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
            "Processing notification {NotificationId} for event {EventType}: {Title}",
            message.NotificationId, message.EventType, message.Title);

        // Check webhook configuration first - no point tracking if we can't send
        var webhookUrl = _configuration["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _logger.LogWarning("Discord:WebhookUrl not configured, skipping notification");
            return;
        }

        // Idempotency check: Use "act-then-check" pattern to avoid race conditions
        // Try to insert a Pending log entry atomically - if another consumer is already processing,
        // the unique constraint on Id will cause a conflict
        var preliminaryLog = new DiscordNotificationLog
        {
            Id = message.NotificationId,
            OrganizationId = message.OrgId,
            EventType = Truncate(message.EventType, 100),
            Channel = "pending",
            Title = Truncate(message.Title, 500),
            Status = NotificationStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _db.NotificationLogs.Add(preliminaryLog);

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
            // Successfully inserted - we have the "lock" on this notification
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Unique constraint violation - another consumer already has this notification
            _db.ChangeTracker.Clear(); // Detach the failed entity

            var existingLog = await _db.NotificationLogs.FindAsync(
                [message.NotificationId],
                context.CancellationToken);

            if (existingLog?.Status == NotificationStatus.Sent)
            {
                _logger.LogInformation(
                    "Notification {NotificationId} already sent, skipping duplicate",
                    message.NotificationId);
                return;
            }

            if (existingLog?.Status == NotificationStatus.Pending)
            {
                // Another consumer is currently processing this notification
                // Let MassTransit redeliver later
                _logger.LogInformation(
                    "Notification {NotificationId} is being processed by another consumer, will retry later",
                    message.NotificationId);
                throw; // Rethrow to trigger MassTransit retry
            }

            // If it failed before, we'll retry - remove the failed log entry
            if (existingLog is not null)
            {
                _logger.LogInformation(
                    "Retrying previously failed notification {NotificationId}",
                    message.NotificationId);
                _db.NotificationLogs.Remove(existingLog);
                await _db.SaveChangesAsync(context.CancellationToken);

                // Re-add the preliminary log for this attempt
                preliminaryLog = new DiscordNotificationLog
                {
                    Id = message.NotificationId,
                    OrganizationId = message.OrgId,
                    EventType = Truncate(message.EventType, 100),
                    Channel = "pending",
                    Title = Truncate(message.Title, 500),
                    Status = NotificationStatus.Pending,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                _db.NotificationLogs.Add(preliminaryLog);
                await _db.SaveChangesAsync(context.CancellationToken);
            }
        }

        // Determine which channel based on event type
        var channel = GetChannelForEvent(message.EventType, message.Severity);

        // Build the Discord embed
        var embed = BuildEmbed(message);
        var payload = new { embeds = new[] { embed } };
        var jsonPayload = JsonSerializer.Serialize(payload);

        // HttpClient from IHttpClientFactory should not be disposed - the factory manages the lifetime
#pragma warning disable CA2000 // The factory manages HttpClient lifetime
        var httpClient = _httpClientFactory.CreateClient();
#pragma warning restore CA2000

        try
        {
            // Send with rate limit handling
            var (response, attempt) = await SendWithRateLimitRetryAsync(
                httpClient,
                webhookUrl,
                jsonPayload,
                context.CancellationToken);

            try
            {
                // Update the preliminary log entry with final status
                preliminaryLog.Channel = Truncate(channel, 100);
                preliminaryLog.Status = response.IsSuccessStatusCode ? NotificationStatus.Sent : NotificationStatus.Failed;
                preliminaryLog.ErrorMessage = response.IsSuccessStatusCode ? null : Truncate($"HTTP {response.StatusCode} (attempt {attempt})", 1000);

                await _db.SaveChangesAsync(context.CancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "Successfully sent notification to Discord (attempt {Attempt})",
                        attempt);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(context.CancellationToken);
                    _logger.LogWarning(
                        "Failed to send notification to Discord after {Attempts} attempts: {StatusCode} - {Error}",
                        attempt, response.StatusCode, errorBody);
                }
            }
            finally
            {
                response.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Rethrow cancellation to allow MassTransit to handle it properly
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending notification to Discord");

            // Update the preliminary log entry with failure status
            preliminaryLog.Channel = Truncate(channel, 100);
            preliminaryLog.Status = NotificationStatus.Failed;
            preliminaryLog.ErrorMessage = Truncate(ex.Message, 1000);
            await _db.SaveChangesAsync(context.CancellationToken);
        }
    }

    /// <summary>
    /// Sends a request with automatic retry on rate limits (HTTP 429).
    /// Respects Discord's Retry-After header and uses exponential backoff.
    /// </summary>
    private async Task<(HttpResponseMessage Response, int Attempt)> SendWithRateLimitRetryAsync(
        HttpClient httpClient,
        string webhookUrl,
        string jsonPayload,
        CancellationToken ct)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            response = await httpClient.PostAsync(webhookUrl, content, ct);

            // Success or non-retryable error
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return (response, attempt);
            }

            // Rate limited - determine wait time
            var delayMs = GetRetryDelayMs(response, attempt);

            _logger.LogWarning(
                "Discord rate limit hit (attempt {Attempt}/{MaxAttempts}). Waiting {DelayMs}ms before retry",
                attempt, MaxRetryAttempts, delayMs);

            // Don't retry if this is the last attempt
            if (attempt >= MaxRetryAttempts)
            {
                break;
            }

            // Dispose the 429 response before retrying
            response.Dispose();

            await Task.Delay(delayMs, ct);
        }

        return (response!, MaxRetryAttempts);
    }

    /// <summary>
    /// Gets the delay before retrying, respecting Discord's Retry-After header.
    /// Falls back to exponential backoff if header is not present.
    /// </summary>
    private static int GetRetryDelayMs(HttpResponseMessage response, int attempt)
    {
        // Check for Retry-After header (seconds or date)
        if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
        {
            var retryAfter = retryAfterValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(retryAfter))
            {
                // Try to parse as seconds
                if (int.TryParse(retryAfter, out var seconds))
                {
                    return Math.Min(seconds * 1000, MaxDelayMs);
                }

                // Try to parse as HTTP date
                if (DateTimeOffset.TryParse(retryAfter, out var retryDate))
                {
                    var delay = (int)(retryDate - DateTimeOffset.UtcNow).TotalMilliseconds;
                    return Math.Clamp(delay, BaseDelayMs, MaxDelayMs);
                }
            }
        }

        // Exponential backoff: 1s, 2s, 4s, etc.
        var exponentialDelay = BaseDelayMs * (1 << (attempt - 1));
        return Math.Min(exponentialDelay, MaxDelayMs);
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

    private static Dictionary<string, object> BuildEmbed(SendDiscordNotification message)
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
