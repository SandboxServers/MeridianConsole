namespace Dhadgar.Contracts.Messaging;

/// <summary>
/// Marker interface for integration events that cross service boundaries via MassTransit.
/// </summary>
/// <remarks>
/// <para>
/// Integration events are published by one service and consumed by others. They represent
/// facts that have occurred in the domain (past tense naming: UserCreated, OrderShipped).
/// </para>
/// <para>
/// Implementing this interface provides:
/// <list type="bullet">
///   <item><description>Common base for consumer constraints</description></item>
///   <item><description>Consistent event identification via EventId</description></item>
///   <item><description>Standardized timestamp via OccurredAtUtc</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Migration Note:</b> Existing events (NodeEvents, IdentityEvents) predate this interface.
/// New events should implement IIntegrationEvent. Existing events can be migrated gradually.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public record ServerProvisioned(
///     Guid EventId,
///     Guid ServerId,
///     Guid NodeId,
///     string ServerName,
///     DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
/// </code>
/// </example>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the unique identifier for this event instance.
    /// </summary>
    /// <remarks>
    /// Used for idempotency checks and event deduplication. Generate with <c>Guid.NewGuid()</c>
    /// at the time of event creation (not at publish time).
    /// </remarks>
    Guid EventId { get; }

    /// <summary>
    /// Gets the UTC timestamp when this event occurred.
    /// </summary>
    /// <remarks>
    /// Represents when the domain event happened, not when it was published.
    /// Use <c>DateTimeOffset.UtcNow</c> at creation time.
    /// </remarks>
    DateTimeOffset OccurredAtUtc { get; }
}
