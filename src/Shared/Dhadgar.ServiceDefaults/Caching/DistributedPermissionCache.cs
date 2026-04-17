using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.ServiceDefaults.Caching;

/// <summary>
/// Configuration options for permission caching.
/// </summary>
public sealed class PermissionCacheOptions
{
    /// <summary>
    /// Cache key prefix. Default is "permissions".
    /// </summary>
    public string KeyPrefix { get; set; } = "permissions";

    /// <summary>
    /// Time-to-live for cached permissions. Default is 5 minutes.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether caching is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Permission cache implementation using IDistributedCache.
/// Uses Redis or any IDistributedCache implementation.
/// </summary>
public sealed class DistributedPermissionCache : IPermissionCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedPermissionCache> _logger;
    private readonly PermissionCacheOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DistributedPermissionCache(
        IDistributedCache cache,
        ILogger<DistributedPermissionCache> logger,
        IOptions<PermissionCacheOptions> options)
    {
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyCollection<string>?> GetPermissionsAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        try
        {
            var key = BuildKey(userId, organizationId);
            var cached = await _cache.GetStringAsync(key, ct);

            if (string.IsNullOrWhiteSpace(cached))
            {
                return null;
            }

            var permissions = JsonSerializer.Deserialize<string[]>(cached, JsonOptions);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Permission cache hit for user {UserId} in org {OrgId}", userId, organizationId);
            }
            return permissions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading permission cache for user {UserId} in org {OrgId}", userId, organizationId);
            return null;
        }
    }

    public async Task SetPermissionsAsync(
        Guid userId,
        Guid organizationId,
        IReadOnlyCollection<string> permissions,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            var key = BuildKey(userId, organizationId);
            var value = JsonSerializer.Serialize(permissions.ToArray(), JsonOptions);

            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.CacheDuration
            };

            await _cache.SetStringAsync(key, value, cacheOptions, ct);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Cached {Count} permissions for user {UserId} in org {OrgId}", permissions.Count, userId, organizationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error caching permissions for user {UserId} in org {OrgId}", userId, organizationId);
        }
    }

    public async Task InvalidateAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            var key = BuildKey(userId, organizationId);
            await _cache.RemoveAsync(key, ct);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Invalidated permission cache for user {UserId} in org {OrgId}", userId, organizationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating permission cache for user {UserId} in org {OrgId}", userId, organizationId);
        }
    }

    public Task InvalidateUserAsync(Guid userId, CancellationToken ct = default)
    {
        // Note: With distributed cache, we can't efficiently enumerate keys by pattern.
        // For a production system, consider using Redis SCAN with pattern matching
        // or maintaining a set of organization IDs per user.
        // For now, this is a no-op and relies on TTL expiration.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "InvalidateUserAsync called for user {UserId} - relying on TTL expiration " +
                "(pattern-based invalidation requires Redis-specific implementation)",
                userId);
        }
        return Task.CompletedTask;
    }

    public Task InvalidateOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        // Note: Same limitation as InvalidateUserAsync.
        // Consider maintaining a list of active users per org for pattern invalidation.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "InvalidateOrganizationAsync called for org {OrgId} - relying on TTL expiration " +
                "(pattern-based invalidation requires Redis-specific implementation)",
                organizationId);
        }
        return Task.CompletedTask;
    }

    private string BuildKey(Guid userId, Guid organizationId)
    {
        return $"{_options.KeyPrefix}:{userId}:{organizationId}";
    }
}
