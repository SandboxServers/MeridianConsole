using Dhadgar.Contracts.Notifications;
using Dhadgar.Contracts.Servers;
using Dhadgar.Notifications.Services;
using MassTransit;

namespace Dhadgar.Notifications.Consumers;

/// <summary>
/// Consumes ServerStarted events and dispatches notifications.
/// </summary>
public sealed class ServerStartedConsumer : IConsumer<ServerStarted>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<ServerStartedConsumer> _logger;

    public ServerStartedConsumer(
        INotificationDispatcher dispatcher,
        ILogger<ServerStartedConsumer> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ServerStarted> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing ServerStarted event for server {ServerId} ({ServerName}) in org {OrgId}",
            message.ServerId, message.ServerName, message.OrgId);

        await _dispatcher.DispatchAsync(
            message.OrgId,
            message.ServerId,
            new NotificationContext(
                Title: $"Server '{message.ServerName}' is now online",
                Message: $"Your {message.GameType} server started successfully.",
                Severity: NotificationSeverity.Info,
                EventType: NotificationEventTypes.ServerStarted,
                RelatedEntityType: "server",
                RelatedEntityId: message.ServerId,
                Fields: new Dictionary<string, string>
                {
                    ["Server"] = message.ServerName,
                    ["Game"] = message.GameType,
                    ["Started At"] = message.OccurredAtUtc.ToString("g", System.Globalization.CultureInfo.InvariantCulture)
                }),
            context.CancellationToken);
    }
}
