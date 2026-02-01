using MassTransit;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Messaging.Publishing;

/// <summary>
/// Default implementation of <see cref="IEventPublisher"/> that delegates to MassTransit's
/// <see cref="IPublishEndpoint"/>.
/// </summary>
/// <remarks>
/// <para>
/// This implementation adds logging around publish operations while preserving
/// MassTransit's behavior. For transactional outbox scenarios, the underlying
/// <c>IPublishEndpoint</c> should be scoped to the DbContext.
/// </para>
/// </remarks>
public sealed class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitEventPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MassTransitEventPublisher"/> class.
    /// </summary>
    /// <param name="publishEndpoint">The MassTransit publish endpoint.</param>
    /// <param name="logger">The logger instance.</param>
    public MassTransitEventPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(TEvent).Name;

        _logger.LogDebug("Publishing {EventType} event", eventType);

        try
        {
            await _publishEndpoint.Publish(@event, ct);

            _logger.LogDebug("Successfully published {EventType} event", eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {EventType} event: {ErrorMessage}",
                eventType, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PublishBatchAsync<TEvent>(IEnumerable<TEvent> events, CancellationToken ct = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(events);

        var eventType = typeof(TEvent).Name;
        var eventList = events.ToList();

        if (eventList.Count == 0)
        {
            _logger.LogDebug("Skipping empty batch publish for {EventType}", eventType);
            return;
        }

        _logger.LogDebug("Publishing batch of {Count} {EventType} events", eventList.Count, eventType);

        try
        {
            // Use MassTransit's batch extension which uses Task.WhenAll internally
            // for better performance than sequential awaits
            await _publishEndpoint.PublishBatch(eventList, ct);

            _logger.LogDebug("Successfully published batch of {Count} {EventType} events",
                eventList.Count, eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch of {EventType} events ({TotalCount} total): {ErrorMessage}",
                eventType, eventList.Count, ex.Message);
            throw;
        }
    }
}
