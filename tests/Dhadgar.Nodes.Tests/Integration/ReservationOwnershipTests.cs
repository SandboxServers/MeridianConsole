using System.Net;
using System.Net.Http.Json;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Nodes.Tests.Integration;

[Collection("Nodes Integration")]
public sealed class ReservationOwnershipTests
{
    private readonly NodesWebApplicationFactory _factory;
    private static readonly Guid TestUserId = Guid.NewGuid();

    public ReservationOwnershipTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<Node> SeedNodeWithCapacityAsync(Guid organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        await db.Database.EnsureCreatedAsync();

        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = $"node-{Guid.NewGuid():N}".Substring(0, 20),
            DisplayName = "Ownership Node",
            Status = NodeStatus.Online,
            Platform = "linux",
            AgentVersion = "1.0.0",
            LastHeartbeat = _factory.TimeProvider.GetUtcNow().UtcDateTime,
            CreatedAt = _factory.TimeProvider.GetUtcNow().UtcDateTime
        };

        var hardware = new NodeHardwareInventory
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Hostname = "test-host",
            CpuCores = 8,
            MemoryBytes = 32L * 1024 * 1024 * 1024,
            DiskBytes = 1000L * 1024 * 1024 * 1024,
            CollectedAt = _factory.TimeProvider.GetUtcNow().UtcDateTime
        };

        var capacity = new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            MaxGameServers = 10,
            CurrentGameServers = 2,
            AvailableMemoryBytes = 16L * 1024 * 1024 * 1024,
            AvailableDiskBytes = 500L * 1024 * 1024 * 1024,
            UpdatedAt = _factory.TimeProvider.GetUtcNow().UtcDateTime
        };

        db.Nodes.Add(node);
        db.HardwareInventories.Add(hardware);
        db.NodeCapacities.Add(capacity);
        await db.SaveChangesAsync();

        return node;
    }

    /// <summary>
    /// Creates a reservation on the node as a user of the node's organization and returns its token.
    /// </summary>
    private async Task<Guid> CreateReservationAsync(Guid organizationId, Node node)
    {
        using var client = _factory.CreateAuthenticatedClient(TestUserId, organizationId);
        var response = await client.PostAsJsonAsync(
            $"/organizations/{organizationId}/nodes/{node.Id}/reservations",
            new
            {
                MemoryMb = 1024,
                DiskMb = 10240,
                CpuMillicores = 1000,
                TtlMinutes = 15,
                RequestedBy = "tasks-service"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reservation = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(reservation);
        return reservation.ReservationToken;
    }

    private async Task<ReservationStatus?> ReadStatusThroughServiceAsync(Guid token)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();
        var result = await service.GetByTokenAsync(token);
        return result.Value?.Status;
    }

    [Fact]
    public async Task CrossOrgCaller_GetClaimAndDelete_ReturnNotFound_AndReservationStaysPending()
    {
        // Arrange
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgA);
        var token = await CreateReservationAsync(orgA, node);
        using var otherOrgClient = _factory.CreateAuthenticatedClient(TestUserId, orgB);

        // Act
        var getResponse = await otherOrgClient.GetAsync($"/reservations/{token}");
        var claimResponse = await otherOrgClient.PostAsJsonAsync(
            $"/reservations/{token}/claim",
            new { ServerId = Guid.NewGuid() });
        var deleteResponse = await otherOrgClient.DeleteAsync($"/reservations/{token}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, claimResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
        Assert.Equal(ReservationStatus.Pending, await ReadStatusThroughServiceAsync(token));
    }

    [Fact]
    public async Task OwningOrgCaller_GetClaimAndDelete_ReturnOkOkAndNoContent()
    {
        // Arrange
        var orgA = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgA);
        var token = await CreateReservationAsync(orgA, node);
        using var ownerClient = _factory.CreateAuthenticatedClient(TestUserId, orgA);

        // Act
        var getResponse = await ownerClient.GetAsync($"/reservations/{token}");
        var claimResponse = await ownerClient.PostAsJsonAsync(
            $"/reservations/{token}/claim",
            new { ServerId = Guid.NewGuid() });
        var deleteResponse = await ownerClient.DeleteAsync($"/reservations/{token}?reason=test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task CallerWithoutOrgClaim_Claim_ReturnsForbidden()
    {
        // Arrange
        var orgA = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgA);
        var token = await CreateReservationAsync(orgA, node);
        using var noOrgClient = _factory.CreateAuthenticatedClient(TestUserId);

        // Act
        var response = await noOrgClient.PostAsJsonAsync(
            $"/reservations/{token}/claim",
            new { ServerId = Guid.NewGuid() });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ReservationStatus.Pending, await ReadStatusThroughServiceAsync(token));
    }

    [Fact]
    public async Task AgentShapedPrincipal_WithOrgClaim_Claim_ReturnsForbidden()
    {
        // Arrange
        var orgA = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgA);
        var token = await CreateReservationAsync(orgA, node);
        using var agentClient = _factory.CreateAuthenticatedClient(TestUserId, orgA);
        agentClient.DefaultRequestHeaders.Add(TestNodesAuthHandler.NodeIdHeader, node.Id.ToString());

        // Act
        var response = await agentClient.PostAsJsonAsync(
            $"/reservations/{token}/claim",
            new { ServerId = Guid.NewGuid() });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ReservationStatus.Pending, await ReadStatusThroughServiceAsync(token));
    }

    [Fact]
    public async Task UnknownToken_ForOrgCaller_ReturnsNotFound()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient(TestUserId, Guid.NewGuid());

        // Act
        var response = await client.GetAsync($"/reservations/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
