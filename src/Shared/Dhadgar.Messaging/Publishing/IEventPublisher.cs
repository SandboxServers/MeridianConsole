namespace Dhadgar.Messaging.Publishing;

/// <summary>
/// Abstraction for publishing integration events via MassTransit.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides a consistent API for publishing events across services.
/// It wraps MassTransit's <c>IPublishEndpoint</c> with standardized patterns.
/// </para>
/// <para>
/// <b>Why use this over IPublishEndpoint directly?</b>
/// <list type="bullet">
///   <item><description>Easier to mock in unit tests</description></item>
///   <item><description>Can add cross-cutting concerns (logging, metrics)</description></item>
///   <item><description>Consistent API across the codebase</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Note:</b> For transactional publishing with EF Core outbox, continue using
/// <c>IPublishEndpoint</c> directly within the DbContext transaction scope.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class OrderService
/// {
///     private readonly IEventPublisher _events;
///
///     public async Task CompleteOrder(Order order, CancellationToken ct)
///     {
///         // ... complete order logic ...
///
///         await _events.PublishAsync(new OrderCompleted(
///             EventId: Guid.NewGuid(),
///             OrderId: order.Id,
///             OccurredAtUtc: DateTimeOffset.UtcNow), ct);
///     }
/// }
/// </code>
/// </example>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event to all subscribed consumers.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="event">The event instance to publish.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class;

    /// <summary>
    /// Publishes multiple events to all subscribed consumers.
    /// </summary>
    /// <typeparam name="TEvent">The type of events to publish.</typeparam>
    /// <param name="events">The event instances to publish.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishBatchAsync<TEvent>(IEnumerable<TEvent> events, CancellationToken ct = default)
        where TEvent : class;
}
