using Dhadgar.ServiceDefaults.Caching;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Decorator that adds caching to the permission service.
/// Wraps the underlying PermissionService and caches results.
/// </summary>
public sealed class CachedPermissionService : IPermissionService
{
    private readonly PermissionService _inner;
    private readonly IPermissionCache _cache;

    public CachedPermissionService(
        PermissionService inner,
        IPermissionCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<IReadOnlyCollection<string>> CalculatePermissionsAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        // Try to get from cache first
        var cached = await _cache.GetPermissionsAsync(userId, organizationId, ct);
        if (cached is not null)
        {
            return cached;
        }

        // Calculate permissions from database
        var permissions = await _inner.CalculatePermissionsAsync(userId, organizationId, ct);

        // Cache the result
        await _cache.SetPermissionsAsync(userId, organizationId, permissions, ct);

        return permissions;
    }
}

/// <summary>
/// Extension methods for registering cached permission service.
/// </summary>
public static class CachedPermissionServiceExtensions
{
    /// <summary>
    /// Registers the permission cache and cached permission service.
    /// If not called, the standard PermissionService is used without caching.
    /// </summary>
    public static IServiceCollection AddPermissionCaching(
        this IServiceCollection services,
        Action<PermissionCacheOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<PermissionCacheOptions>(_ => { });
        }

        // Register the cache implementation
        services.AddSingleton<IPermissionCache, DistributedPermissionCache>();

        // Use decorator pattern: replace IPermissionService registration
        // First ensure PermissionService itself is registered (not as interface)
        services.AddScoped<PermissionService>();

        // Now register the cached version as the interface
        services.AddScoped<IPermissionService, CachedPermissionService>();

        return services;
    }
}
