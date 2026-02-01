using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Dhadgar.Console.Services;

public sealed class ConsoleSessionManager : IConsoleSessionManager
{
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _sessionExpiry = TimeSpan.FromHours(2);

    public ConsoleSessionManager(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task AddConnectionAsync(string connectionId, Guid serverId, Guid organizationId, Guid? userId, CancellationToken ct = default)
    {
        // Add connection to server's connection set
        var serverKey = GetServerConnectionsKey(serverId);
        var connections = await GetSetAsync<string>(serverKey, ct);
        connections.Add(connectionId);
        await SetSetAsync(serverKey, connections, ct);

        // Add server to connection's server set
        var connectionKey = GetConnectionServersKey(connectionId);
        var servers = await GetSetAsync<Guid>(connectionKey, ct);
        servers.Add(serverId);
        await SetSetAsync(connectionKey, servers, ct);

        // Store connection metadata
        var metadataKey = GetConnectionMetadataKey(connectionId, serverId);
        var metadata = new ConnectionMetadata(organizationId, userId, DateTime.UtcNow);
        await _cache.SetStringAsync(metadataKey, JsonSerializer.Serialize(metadata),
            new DistributedCacheEntryOptions { SlidingExpiration = _sessionExpiry }, ct);
    }

    public async Task RemoveConnectionAsync(string connectionId, Guid serverId, CancellationToken ct = default)
    {
        // Remove connection from server's connection set
        var serverKey = GetServerConnectionsKey(serverId);
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

        // Remove server from connection's server set
        var connectionKey = GetConnectionServersKey(connectionId);
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
        var connections = await GetSetAsync<string>(serverKey, ct);
        return connections.ToList();
    }

    public async Task<bool> IsConnectedToServerAsync(string connectionId, Guid serverId, CancellationToken ct = default)
    {
        var serverKey = GetServerConnectionsKey(serverId);
        var connections = await GetSetAsync<string>(serverKey, ct);
        return connections.Contains(connectionId);
    }

    public async Task<IReadOnlyList<Guid>> GetConnectionServersAsync(string connectionId, CancellationToken ct = default)
    {
        var connectionKey = GetConnectionServersKey(connectionId);
        var servers = await GetSetAsync<Guid>(connectionKey, ct);
        return servers.ToList();
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
