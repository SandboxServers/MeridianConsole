namespace Dhadgar.Shared.Data;

/// <summary>
/// Interface for entities that require audit tracking but cannot inherit from <see cref="BaseEntity"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use this interface for entities that must extend another base class (such as ASP.NET Identity's
/// <c>IdentityUser</c>) but still need standard audit timestamp tracking.
/// </para>
/// <para>
/// The <see cref="DhadgarDbContext"/> automatically sets <see cref="UpdatedAt"/> on entities
/// implementing this interface when changes are detected during <c>SaveChangesAsync</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class User : IdentityUser&lt;Guid&gt;, IAuditableEntity
/// {
///     public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
///     public DateTime? UpdatedAt { get; set; }
///     public DateTime? DeletedAt { get; set; }
/// }
/// </code>
/// </example>
public interface IAuditableEntity
{
    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was created.
    /// </summary>
    DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was last updated.
    /// </summary>
    /// <remarks>
    /// Null indicates the entity has never been modified since creation.
    /// </remarks>
    DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was soft-deleted.
    /// </summary>
    /// <remarks>
    /// When non-null, the entity is considered deleted and may be excluded from queries
    /// depending on the query filter configuration.
    /// </remarks>
    DateTime? DeletedAt { get; set; }
}
