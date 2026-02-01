using Dhadgar.Contracts.Notifications;
using Dhadgar.Contracts.Servers;
using Dhadgar.Notifications.Services;
using MassTransit;

namespace Dhadgar.Notifications.Consumers;

/// <summary>
/// Consumes ServerStopped events and dispatches notifications.
/// </summary>
public sealed class ServerStoppedConsumer : IConsumer<ServerStopped>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<ServerStoppedConsumer> _logger;

    public ServerStoppedConsumer(
        INotificationDispatcher dispatcher,
        ILogger<ServerStoppedConsumer> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ServerStopped> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing ServerStopped event for server {ServerId} ({ServerName}) in org {OrgId}",
            message.ServerId, message.ServerName, message.OrgId);

        await _dispatcher.DispatchAsync(
            message.OrgId,
            message.ServerId,
            new NotificationContext(
                Title: $"Server '{message.ServerName}' has stopped",
                Message: $"Your server was stopped. Reason: {message.Reason}",
                Severity: NotificationSeverity.Info,
                EventType: NotificationEventTypes.ServerStopped,
                RelatedEntityType: "server",
                RelatedEntityId: message.ServerId,
                Fields: new Dictionary<string, string>
                {
                    ["Server"] = message.ServerName,
                    ["Reason"] = message.Reason,
                    ["Stopped At"] = message.OccurredAtUtc.ToString("g", System.Globalization.CultureInfo.InvariantCulture)
                }),
            context.CancellationToken);
    }
}
