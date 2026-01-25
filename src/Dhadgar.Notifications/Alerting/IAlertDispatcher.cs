namespace Dhadgar.Notifications.Alerting;

/// <summary>
/// Dispatches alert messages to configured notification channels (Discord, email, etc.).
/// </summary>
public interface IAlertDispatcher
{
    /// <summary>
    /// Dispatches an alert to all configured channels.
    /// </summary>
    /// <param name="alert">The alert message to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task DispatchAsync(AlertMessage alert, CancellationToken cancellationToken = default);
}
