namespace Dhadgar.ServiceDefaults.Caching;

/// <summary>
/// Cache for user permissions within an organization.
/// Implementations should handle cache invalidation on permission changes.
/// </summary>
public interface IPermissionCache
{
    /// <summary>
    /// Gets cached permissions for a user in an organization.
    /// Returns null if not cached.
    /// </summary>
    Task<IReadOnlyCollection<string>?> GetPermissionsAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Stores permissions in cache.
    /// </summary>
    Task SetPermissionsAsync(
        Guid userId,
        Guid organizationId,
        IReadOnlyCollection<string> permissions,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached permissions for a user in an organization.
    /// Call this when permissions change (role assignment, custom claims, etc.).
    /// </summary>
    Task InvalidateAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached permissions for a user across all organizations.
    /// Call this when user is deactivated or deleted.
    /// </summary>
    Task InvalidateUserAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached permissions for an organization.
    /// Call this when organization-wide permission changes occur (role definition changes).
    /// </summary>
    Task InvalidateOrganizationAsync(
        Guid organizationId,
        CancellationToken ct = default);
}
