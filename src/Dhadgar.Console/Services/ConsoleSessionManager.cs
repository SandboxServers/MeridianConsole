using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Text.Json;

namespace Dhadgar.Console.Services;

public sealed class ConsoleSessionManager : IConsoleSessionManager
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeSpan _sessionExpiry = TimeSpan.FromHours(2);

    public ConsoleSessionManager(IDistributedCache cache, IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _redis = redis;
    }

    public async Task AddConnectionAsync(string connectionId, Guid serverId, Guid organizationId, Guid? userId, CancellationToken ct = default)
    {
        var serverKey = GetServerConnectionsKey(serverId);
        var connectionKey = GetConnectionServersKey(connectionId);

        if (_redis != null)
        {
            // Use atomic Redis SADD operations to prevent race conditions
            var db = _redis.GetDatabase();
            var batch = db.CreateBatch();

            // Add connection to server's connection set atomically
            var addToServerTask = batch.SetAddAsync(serverKey, connectionId);
            var setServerExpiryTask = batch.KeyExpireAsync(serverKey, _sessionExpiry);

            // Add server to connection's server set atomically
            var addToConnectionTask = batch.SetAddAsync(connectionKey, serverId.ToString());
            var setConnectionExpiryTask = batch.KeyExpireAsync(connectionKey, _sessionExpiry);

            batch.Execute();
            await Task.WhenAll(addToServerTask, setServerExpiryTask, addToConnectionTask, setConnectionExpiryTask);
        }
        else
        {
            // Fallback for non-Redis cache (tests, etc.) - has race condition but works
            var connections = await GetSetAsync<string>(serverKey, ct);
            connections.Add(connectionId);
            await SetSetAsync(serverKey, connections, ct);

            var servers = await GetSetAsync<Guid>(connectionKey, ct);
            servers.Add(serverId);
            await SetSetAsync(connectionKey, servers, ct);
        }

        // Store connection metadata (single key, no race condition)
        var metadataKey = GetConnectionMetadataKey(connectionId, serverId);
        var metadata = new ConnectionMetadata(organizationId, userId, DateTime.UtcNow);
        await _cache.SetStringAsync(metadataKey, JsonSerializer.Serialize(metadata),
            new DistributedCacheEntryOptions { SlidingExpiration = _sessionExpiry }, ct);
    }

    public async Task RemoveConnectionAsync(string connectionId, Guid serverId, CancellationToken ct = default)
    {
        var serverKey = GetServerConnectionsKey(serverId);
        var connectionKey = GetConnectionServersKey(connectionId);

        if (_redis != null)
        {
            // Use atomic Redis SREM operations to prevent race conditions
            var db = _redis.GetDatabase();
            var batch = db.CreateBatch();

            // Remove connection from server's connection set atomically
            var removeFromServerTask = batch.SetRemoveAsync(serverKey, connectionId);

            // Remove server from connection's server set atomically
            var removeFromConnectionTask = batch.SetRemoveAsync(connectionKey, serverId.ToString());

            batch.Execute();
            await Task.WhenAll(removeFromServerTask, removeFromConnectionTask);
        }
        else
        {
            // Fallback for non-Redis cache
            var connections = await GetSetAsync<string>(serverKey, ct);
            connections.Remove(connectionId);
            if (connections.Count > 0)
            {
                await SetSetAsync(serverKey, connections, ct);
            }
            else
            {
                await _cache.RemoveAsync(serverKey, ct);
            }

            var servers = await GetSetAsync<Guid>(connectionKey, ct);
            servers.Remove(serverId);
            if (servers.Count > 0)
            {
                await SetSetAsync(connectionKey, servers, ct);
            }
            else
            {
                await _cache.RemoveAsync(connectionKey, ct);
            }
        }

        // Remove metadata
        var metadataKey = GetConnectionMetadataKey(connectionId, serverId);
        await _cache.RemoveAsync(metadataKey, ct);
    }

    public async Task RemoveAllConnectionsAsync(string connectionId, CancellationToken ct = default)
    {
        var servers = await GetConnectionServersAsync(connectionId, ct);
        foreach (var serverId in servers)
        {
            await RemoveConnectionAsync(connectionId, serverId, ct);
        }
    }

    public async Task<IReadOnlyList<string>> GetServerConnectionsAsync(Guid serverId, CancellationToken ct = default)
    {
        var serverKey = GetServerConnectionsKey(serverId);

        if (_redis != null)
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(serverKey);
            return members.Select(m => m.ToString()).ToList();
        }

        var connections = await GetSetAsync<string>(serverKey, ct);
        return connections.ToList();
    }

    public async Task<bool> IsConnectedToServerAsync(string connectionId, Guid serverId, CancellationToken ct = default)
    {
        var serverKey = GetServerConnectionsKey(serverId);

        if (_redis != null)
        {
            var db = _redis.GetDatabase();
            return await db.SetContainsAsync(serverKey, connectionId);
        }

        var connections = await GetSetAsync<string>(serverKey, ct);
        return connections.Contains(connectionId);
    }

    public async Task<IReadOnlyList<Guid>> GetConnectionServersAsync(string connectionId, CancellationToken ct = default)
    {
        var connectionKey = GetConnectionServersKey(connectionId);

        if (_redis != null)
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(connectionKey);
            return members
                .Select(m => Guid.TryParse(m.ToString(), out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToList();
        }

        var servers = await GetSetAsync<Guid>(connectionKey, ct);
        return servers.ToList();
    }

    public async Task<(Guid OrganizationId, Guid? UserId, DateTime ConnectedAt)?> GetConnectionMetadataAsync(string connectionId, Guid serverId, CancellationToken ct = default)
    {
        var metadataKey = GetConnectionMetadataKey(connectionId, serverId);
        var data = await _cache.GetStringAsync(metadataKey, ct);

        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize<ConnectionMetadata>(data);
        if (metadata == null)
        {
            return null;
        }

        return (metadata.OrganizationId, metadata.UserId, metadata.ConnectedAt);
    }

    private static string GetServerConnectionsKey(Guid serverId) => $"console:server:{serverId}:connections";
    private static string GetConnectionServersKey(string connectionId) => $"console:connection:{connectionId}:servers";
    private static string GetConnectionMetadataKey(string connectionId, Guid serverId) => $"console:metadata:{connectionId}:{serverId}";

    private async Task<HashSet<T>> GetSetAsync<T>(string key, CancellationToken ct)
    {
        var data = await _cache.GetStringAsync(key, ct);
        if (string.IsNullOrEmpty(data))
        {
            return [];
        }

        return JsonSerializer.Deserialize<HashSet<T>>(data) ?? [];
    }

    private async Task SetSetAsync<T>(string key, HashSet<T> set, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(set);
        await _cache.SetStringAsync(key, data,
            new DistributedCacheEntryOptions { SlidingExpiration = _sessionExpiry }, ct);
    }

    private sealed record ConnectionMetadata(Guid OrganizationId, Guid? UserId, DateTime ConnectedAt);
}
