namespace Dhadgar.Shared.Data;

/// <summary>
/// Marker interface for entities that belong to a specific organization (tenant).
/// </summary>
/// <remarks>
/// <para>
/// Entities implementing this interface are scoped to a single organization and should
/// be filtered by <see cref="OrganizationId"/> in queries to ensure proper data isolation.
/// </para>
/// <para>
/// This interface enables automatic tenant filtering in query extensions and can be used
/// by middleware to enforce tenant boundaries at the database layer.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class GameServer : BaseEntity, ITenantScoped
/// {
///     public Guid OrganizationId { get; set; }
///     public string Name { get; set; } = string.Empty;
///
///     // ITenantScoped implementation
///     Guid ITenantScoped.OrganizationId => OrganizationId;
/// }
/// </code>
/// </example>
public interface ITenantScoped
{
    /// <summary>
    /// Gets the unique identifier of the organization that owns this entity.
    /// </summary>
    /// <remarks>
    /// This property is read-only to prevent accidental reassignment of tenant ownership.
    /// The organization ID should be set once during entity creation and never modified.
    /// </remarks>
    Guid OrganizationId { get; }
}
