namespace Dhadgar.Shared.Data;

/// <summary>
/// Base class for all entities in the platform that supports common audit fields and optimistic concurrency.
/// </summary>
/// <remarks>
/// <para>
/// This base class provides standard audit tracking properties (CreatedAt, UpdatedAt, DeletedAt)
/// and an optimistic concurrency token (RowVersion) that works with PostgreSQL's xmin system column.
/// </para>
/// <para>
/// For entities that cannot inherit from this class (e.g., those extending IdentityUser),
/// implement <see cref="IAuditableEntity"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class GameServer : BaseEntity
/// {
///     public string Name { get; set; } = string.Empty;
///     public Guid OrganizationId { get; set; }
/// }
/// </code>
/// </example>
public abstract class BaseEntity : IAuditableEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity.
    /// </summary>
    /// <remarks>
    /// Uses GUID for globally unique identification across distributed systems.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was created.
    /// </summary>
    /// <remarks>
    /// Automatically set by <see cref="DhadgarDbContext.SaveChangesAsync"/> when the entity is first persisted.
    /// This value should not be modified after initial creation.
    /// </remarks>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was last updated.
    /// </summary>
    /// <remarks>
    /// Automatically set by <see cref="DhadgarDbContext.SaveChangesAsync"/> when modifications are detected.
    /// Null indicates the entity has never been modified since creation.
    /// </remarks>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was soft-deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When this value is non-null, the entity is considered deleted and will be excluded
    /// from queries by the global query filter applied in <see cref="DhadgarDbContext"/>.
    /// </para>
    /// <para>
    /// Use <c>IgnoreQueryFilters()</c> when you need to include soft-deleted records.
    /// </para>
    /// </remarks>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Gets or sets the optimistic concurrency token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In PostgreSQL, this maps to the <c>xmin</c> system column which automatically updates
    /// on every row modification, providing optimistic concurrency control without explicit versioning.
    /// </para>
    /// <para>
    /// For non-PostgreSQL providers (InMemory, SQLite), this is configured as a standard
    /// auto-incrementing version column.
    /// </para>
    /// </remarks>
    public uint RowVersion { get; set; }
}
