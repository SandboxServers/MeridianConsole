using Dhadgar.Console.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Dhadgar.Console.Tests.Services;

public class ConsoleSessionManagerTests
{
    private readonly ConsoleSessionManager _manager;

    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string ConnectionId = "test-connection-1";

    public ConsoleSessionManagerTests()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        // Pass null for IConnectionMultiplexer to use the IDistributedCache fallback path
        _manager = new ConsoleSessionManager(cache, redis: null);
    }

    [Fact]
    public async Task AddConnection_StoresConnectionForServer()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        var connections = await _manager.GetServerConnectionsAsync(ServerId);
        connections.Should().Contain(ConnectionId);
    }

    [Fact]
    public async Task AddConnection_StoresServerForConnection()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        var servers = await _manager.GetConnectionServersAsync(ConnectionId);
        servers.Should().Contain(ServerId);
    }

    [Fact]
    public async Task AddConnection_StoresMetadata()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        var metadata = await _manager.GetConnectionMetadataAsync(ConnectionId, ServerId);
        metadata.Should().NotBeNull();
        metadata!.Value.OrganizationId.Should().Be(OrgId);
        metadata.Value.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task RemoveConnection_RemovesFromServer()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        await _manager.RemoveConnectionAsync(ConnectionId, ServerId);

        var connections = await _manager.GetServerConnectionsAsync(ServerId);
        connections.Should().NotContain(ConnectionId);
    }

    [Fact]
    public async Task RemoveConnection_RemovesFromConnection()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        await _manager.RemoveConnectionAsync(ConnectionId, ServerId);

        var servers = await _manager.GetConnectionServersAsync(ConnectionId);
        servers.Should().NotContain(ServerId);
    }

    [Fact]
    public async Task RemoveConnection_RemovesMetadata()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        await _manager.RemoveConnectionAsync(ConnectionId, ServerId);

        var metadata = await _manager.GetConnectionMetadataAsync(ConnectionId, ServerId);
        metadata.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAllConnections_RemovesFromAllServers()
    {
        var serverId2 = Guid.NewGuid();
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);
        await _manager.AddConnectionAsync(ConnectionId, serverId2, OrgId, UserId);

        await _manager.RemoveAllConnectionsAsync(ConnectionId);

        var connections1 = await _manager.GetServerConnectionsAsync(ServerId);
        var connections2 = await _manager.GetServerConnectionsAsync(serverId2);
        connections1.Should().NotContain(ConnectionId);
        connections2.Should().NotContain(ConnectionId);
    }

    [Fact]
    public async Task GetServerConnections_ReturnsAll()
    {
        const string connectionId2 = "test-connection-2";
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);
        await _manager.AddConnectionAsync(connectionId2, ServerId, OrgId, Guid.NewGuid());

        var connections = await _manager.GetServerConnectionsAsync(ServerId);

        connections.Should().HaveCount(2);
        connections.Should().Contain(ConnectionId);
        connections.Should().Contain(connectionId2);
    }

    [Fact]
    public async Task GetServerConnections_Empty_ReturnsEmpty()
    {
        var connections = await _manager.GetServerConnectionsAsync(Guid.NewGuid());

        connections.Should().BeEmpty();
    }

    [Fact]
    public async Task IsConnectedToServer_Connected_ReturnsTrue()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        var isConnected = await _manager.IsConnectedToServerAsync(ConnectionId, ServerId);

        isConnected.Should().BeTrue();
    }

    [Fact]
    public async Task IsConnectedToServer_NotConnected_ReturnsFalse()
    {
        var isConnected = await _manager.IsConnectedToServerAsync("nonexistent", ServerId);

        isConnected.Should().BeFalse();
    }

    [Fact]
    public async Task GetConnectionServers_ReturnsAll()
    {
        var serverId2 = Guid.NewGuid();
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);
        await _manager.AddConnectionAsync(ConnectionId, serverId2, OrgId, UserId);

        var servers = await _manager.GetConnectionServersAsync(ConnectionId);

        servers.Should().HaveCount(2);
        servers.Should().Contain(ServerId);
        servers.Should().Contain(serverId2);
    }

    [Fact]
    public async Task GetConnectionMetadata_ReturnsCorrectData()
    {
        await _manager.AddConnectionAsync(ConnectionId, ServerId, OrgId, UserId);

        var metadata = await _manager.GetConnectionMetadataAsync(ConnectionId, ServerId);

        metadata.Should().NotBeNull();
        metadata!.Value.OrganizationId.Should().Be(OrgId);
        metadata.Value.UserId.Should().Be(UserId);
        metadata.Value.ConnectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetConnectionMetadata_NotFound_ReturnsNull()
    {
        var metadata = await _manager.GetConnectionMetadataAsync("nonexistent", Guid.NewGuid());

        metadata.Should().BeNull();
    }
}
