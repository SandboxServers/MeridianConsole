using Dhadgar.Contracts.Notifications;
using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Data.Entities;
using Dhadgar.Notifications.Services;
using MassTransit;

namespace Dhadgar.Notifications.Consumers;

/// <summary>
/// Consumes SendEmailNotification commands and sends emails via Office 365.
/// </summary>
public sealed class SendEmailNotificationConsumer : IConsumer<SendEmailNotification>
{
    private readonly IEmailProvider _emailProvider;
    private readonly NotificationsDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SendEmailNotificationConsumer> _logger;

    public SendEmailNotificationConsumer(
        IEmailProvider emailProvider,
        NotificationsDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<SendEmailNotificationConsumer> logger)
    {
        _emailProvider = emailProvider;
        _db = db;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendEmailNotification> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "Processing email notification {NotificationId} for {Recipient}",
            msg.NotificationId, msg.RecipientEmail);

        // Log the notification attempt
        var log = new NotificationLog
        {
            Id = msg.NotificationId,
            OrganizationId = msg.OrgId,
            UserId = msg.UserId,
            EventType = msg.EventType,
            Channel = NotificationChannels.Email,
            Title = msg.Subject,
            Message = msg.HtmlBody,
            Status = NotificationStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _db.Logs.Add(log);
        await _db.SaveChangesAsync(context.CancellationToken);

        var result = await _emailProvider.SendEmailAsync(
            msg.RecipientEmail,
            msg.Subject,
            msg.HtmlBody,
            msg.TextBody,
            context.CancellationToken);

        if (result.Success)
        {
            log.Status = NotificationStatus.Sent;
            log.SentAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new NotificationDelivered(
                msg.NotificationId,
                msg.OrgId,
                msg.UserId,
                NotificationChannels.Email,
                msg.EventType,
                DateTimeOffset.UtcNow), context.CancellationToken);

            _logger.LogInformation(
                "Email notification {NotificationId} delivered successfully",
                msg.NotificationId);
        }
        else
        {
            log.Status = NotificationStatus.Failed;
            log.ErrorMessage = result.ErrorMessage;
            log.RetryCount++;
            await _db.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new NotificationDeliveryFailed(
                msg.NotificationId,
                msg.OrgId,
                msg.UserId,
                NotificationChannels.Email,
                msg.EventType,
                result.ErrorMessage ?? "Unknown error",
                log.RetryCount,
                DateTimeOffset.UtcNow), context.CancellationToken);

            _logger.LogWarning(
                "Email notification {NotificationId} failed: {Error}",
                msg.NotificationId, result.ErrorMessage);

            // Let MassTransit retry policies handle retries
            throw new InvalidOperationException($"Failed to send email: {result.ErrorMessage}");
        }
    }
}
