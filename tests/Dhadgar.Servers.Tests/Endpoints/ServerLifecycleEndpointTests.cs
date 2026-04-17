using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Data;
using Dhadgar.Servers.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Servers.Tests.Endpoints;

[Collection("Servers Integration")]
public sealed class ServerLifecycleEndpointTests
{
    private readonly ServersWebApplicationFactory _factory;

    public ServerLifecycleEndpointTests(ServersWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a server directly in the database with the specified status and node assignment.
    /// This bypasses the API to set up lifecycle test preconditions.
    /// </summary>
    private async Task<Server> CreateServerInDbAsync(
        Guid orgId,
        ServerStatus status,
        ServerPowerState powerState,
        Guid? nodeId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServersDbContext>();

        var server = new Server
        {
            OrganizationId = orgId,
            Name = $"lifecycle-{Guid.NewGuid():N}"[..20],
            GameType = "minecraft",
            CpuLimitMillicores = 2000,
            MemoryLimitMb = 4096,
            DiskLimitMb = 10240,
            Status = status,
            PowerState = powerState,
            NodeId = nodeId
        };

        db.Servers.Add(server);
        await db.SaveChangesAsync();
        return server;
    }

    private static string LifecycleUrl(Guid orgId, Guid serverId, string action) =>
        $"/organizations/{orgId}/servers/{serverId}/{action}";

    // --- Start ---

    [Fact]
    public async Task StartServer_FromReady_WithNode_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Ready, ServerPowerState.Off, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "start"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StartServer_FromStopped_WithNode_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Stopped, ServerPowerState.Off, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "start"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StartServer_FromCrashed_WithNode_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Crashed, ServerPowerState.Crashed, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "start"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StartServer_NoNode_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        // No nodeId assigned
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Ready, ServerPowerState.Off, nodeId: null);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "start"), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StartServer_FromRunning_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Running, ServerPowerState.On, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "start"), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StartServer_NotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.PostAsync(LifecycleUrl(orgId, Guid.NewGuid(), "start"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- Stop ---

    [Fact]
    public async Task StopServer_FromRunning_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Running, ServerPowerState.On, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "stop"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StopServer_FromStarting_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Starting, ServerPowerState.Starting, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "stop"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StopServer_FromStopped_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Stopped, ServerPowerState.Off);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "stop"), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- Restart ---

    [Fact]
    public async Task RestartServer_FromRunning_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Running, ServerPowerState.On, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "restart"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RestartServer_FromStopped_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Stopped, ServerPowerState.Off);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "restart"), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- Kill ---

    [Fact]
    public async Task KillServer_FromRunning_Returns204()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Running, ServerPowerState.On, nodeId);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "kill"), null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task KillServer_AlreadyOff_Returns400()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var server = await CreateServerInDbAsync(orgId, ServerStatus.Stopped, ServerPowerState.Off);

        using var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(LifecycleUrl(orgId, server.Id, "kill"), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
