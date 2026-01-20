using Dhadgar.Contracts.Notifications;
using Dhadgar.Contracts.Servers;
using Dhadgar.Notifications.Services;
using MassTransit;

namespace Dhadgar.Notifications.Consumers;

/// <summary>
/// Consumes ServerCrashed events and dispatches notifications.
/// </summary>
public sealed class ServerCrashedConsumer : IConsumer<ServerCrashed>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<ServerCrashedConsumer> _logger;

    public ServerCrashedConsumer(
        INotificationDispatcher dispatcher,
        ILogger<ServerCrashedConsumer> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ServerCrashed> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing ServerCrashed event for server {ServerId} ({ServerName}) in org {OrgId}",
            message.ServerId, message.ServerName, message.OrgId);

        var fields = new Dictionary<string, string>
        {
            ["Server"] = message.ServerName,
            ["Error"] = message.ErrorSummary,
            ["Crashed At"] = message.OccurredAtUtc.ToString("g")
        };

        if (message.ExitCode.HasValue)
        {
            fields["Exit Code"] = message.ExitCode.Value.ToString();
        }

        await _dispatcher.DispatchAsync(
            message.OrgId,
            message.ServerId,
            new NotificationContext(
                Title: $"Server '{message.ServerName}' has crashed",
                Message: $"Your server crashed unexpectedly. Error: {message.ErrorSummary}",
                Severity: NotificationSeverity.Error,
                EventType: NotificationEventTypes.ServerCrashed,
                RelatedEntityType: "server",
                RelatedEntityId: message.ServerId,
                Fields: fields),
            context.CancellationToken);
    }
}
