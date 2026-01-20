using Dhadgar.Contracts.Notifications;
using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Data.Entities;
using MassTransit;

namespace Dhadgar.Notifications.Services;

/// <summary>
/// Dispatches notifications to the internal team's Discord.
/// This is for admin/dev notifications - not customer-facing.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly NotificationsDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        NotificationsDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task DispatchAsync(
        Guid orgId,
        Guid? serverId,
        NotificationContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Dispatching internal notification: {EventType} - {Title}",
            context.EventType, context.Title);

        var notificationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Log the notification
        var log = new NotificationLog
        {
            Id = notificationId,
            OrganizationId = orgId,
            EventType = context.EventType,
            Channel = NotificationChannels.Discord,
            Title = context.Title,
            Message = context.Message,
            Status = NotificationStatus.Pending,
            CreatedAtUtc = now,
            RelatedEntityType = context.RelatedEntityType,
            RelatedEntityId = context.RelatedEntityId
        };
        _db.Logs.Add(log);
        await _db.SaveChangesAsync(ct);

        // Always dispatch to Discord for internal team
        var discordMessage = new SendDiscordNotification(
            notificationId,
            orgId,
            serverId,
            context.Title,
            context.Message,
            context.Severity,
            context.EventType,
            context.Fields,
            now);

        await _publishEndpoint.Publish(discordMessage, ct);

        _logger.LogDebug(
            "Published SendDiscordNotification for {NotificationId}",
            notificationId);
    }
}
