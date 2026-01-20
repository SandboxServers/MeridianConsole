namespace Dhadgar.Notifications.Services;

/// <summary>
/// Context for a notification to be dispatched.
/// </summary>
public record NotificationContext(
    string Title,
    string Message,
    string Severity,
    string EventType,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// Dispatches notifications to the internal team's Discord channel.
/// This is for admin/dev notifications - not customer-facing.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Dispatches a notification to the team's Discord channel.
    /// </summary>
    Task DispatchAsync(
        Guid orgId,
        Guid? serverId,
        NotificationContext context,
        CancellationToken ct = default);
}
