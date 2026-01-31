using System.Net;
using System.Net.Http.Json;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Nodes.Tests.Integration;

[Collection("Nodes Integration")]
public sealed class ReservationApiIntegrationTests
{
    private readonly NodesWebApplicationFactory _factory;
    private static readonly Guid TestUserId = Guid.NewGuid();

    public ReservationApiIntegrationTests(NodesWebApplicationFactory factory)
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
            Name = $"test-node-{Guid.NewGuid():N}".Substring(0, 20),
            DisplayName = "Test Node",
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
            MemoryBytes = 32L * 1024 * 1024 * 1024, // 32GB
            DiskBytes = 1000L * 1024 * 1024 * 1024, // 1TB
            CollectedAt = _factory.TimeProvider.GetUtcNow().UtcDateTime
        };

        var capacity = new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            MaxGameServers = 10,
            CurrentGameServers = 2,
            AvailableMemoryBytes = 16L * 1024 * 1024 * 1024, // 16GB
            AvailableDiskBytes = 500L * 1024 * 1024 * 1024, // 500GB
            UpdatedAt = _factory.TimeProvider.GetUtcNow().UtcDateTime
        };

        db.Nodes.Add(node);
        db.HardwareInventories.Add(hardware);
        db.NodeCapacities.Add(capacity);
        await db.SaveChangesAsync();

        return node;
    }

    [Fact]
    public async Task CreateReservation_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var request = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            CpuMillicores = 1000,
            TtlMinutes = 15,
            RequestedBy = "tasks-service"
        };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(result);
        Assert.Equal(node.Id, result.NodeId);
        Assert.Equal(1024, result.MemoryMb);
        Assert.Equal(10240, result.DiskMb);
        Assert.Equal(1000, result.CpuMillicores);
        Assert.Equal("tasks-service", result.RequestedBy);
        Assert.Equal(ReservationStatus.Pending, result.Status);
        Assert.NotEqual(Guid.Empty, result.ReservationToken);
    }

    [Fact]
    public async Task CreateReservation_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var orgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        var request = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            RequestedBy = "test"
        };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{nodeId}/reservations",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_NodeNotFound_ReturnsNotFound()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var request = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            RequestedBy = "test"
        };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{Guid.NewGuid()}/reservations",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_CrossTenant_ReturnsNotFound()
    {
        // Arrange
        var orgId1 = Guid.NewGuid();
        var orgId2 = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId1);

        // Client authenticated to orgId2 tries to create reservation on orgId1's node
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId2);

        var request = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            RequestedBy = "test"
        };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/organizations/{orgId2}/nodes/{node.Id}/reservations",
            request);

        // Assert
        // Should be NotFound because the node doesn't belong to orgId2
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListReservations_ReturnsActiveReservations()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create some reservations
        var request = new
        {
            MemoryMb = 512,
            DiskMb = 1024,
            RequestedBy = "test"
        };

        await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations", request);
        await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations", request);

        // Act
        var response = await client.GetAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<ReservationSummary>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAvailableCapacity_ReturnsCapacity()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Act
        var response = await client.GetAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations/capacity");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AvailableCapacityResponse>();
        Assert.NotNull(result);
        Assert.Equal(node.Id, result.NodeId);
        Assert.True(result.AvailableMemoryBytes > 0);
        Assert.True(result.AvailableDiskBytes > 0);
    }

    [Fact]
    public async Task ClaimReservation_ValidToken_Returns200()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create a reservation
        var createRequest = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            RequestedBy = "tasks-service"
        };
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations",
            createRequest);
        var reservation = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        // Act
        var claimRequest = new { ServerId = "server-123" };
        var response = await client.PostAsJsonAsync(
            $"/reservations/{reservation!.ReservationToken}/claim",
            claimRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(result);
        Assert.Equal(ReservationStatus.Claimed, result.Status);
        Assert.Equal("server-123", result.ServerId);
    }

    [Fact]
    public async Task ClaimReservation_InvalidToken_ReturnsNotFound()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient(TestUserId, Guid.NewGuid());

        var claimRequest = new { ServerId = "server-123" };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/reservations/{Guid.NewGuid()}/claim",
            claimRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseReservation_ValidToken_ReturnsNoContent()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create a reservation
        var createRequest = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            RequestedBy = "tasks-service"
        };
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations",
            createRequest);
        var reservation = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        // Act
        var response = await client.DeleteAsync(
            $"/reservations/{reservation!.ReservationToken}?reason=no+longer+needed");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseReservation_InvalidToken_ReturnsNotFound()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient(TestUserId, Guid.NewGuid());

        // Act
        var response = await client.DeleteAsync(
            $"/reservations/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReservation_ValidToken_ReturnsReservation()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create a reservation
        var createRequest = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            RequestedBy = "tasks-service"
        };
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations",
            createRequest);
        var reservation = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        // Act
        var response = await client.GetAsync(
            $"/reservations/{reservation!.ReservationToken}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(result);
        Assert.Equal(reservation.ReservationToken, result.ReservationToken);
    }

    [Fact]
    public async Task FullReservationWorkflow_ReserveClaimRelease()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var node = await SeedNodeWithCapacityAsync(orgId);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Step 1: Create reservation
        var createRequest = new
        {
            MemoryMb = 1024,
            DiskMb = 10240,
            RequestedBy = "tasks-service"
        };
        var createResponse = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/nodes/{node.Id}/reservations",
            createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var reservation = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal(ReservationStatus.Pending, reservation!.Status);

        // Step 2: Claim reservation
        var claimRequest = new { ServerId = "game-server-1" };
        var claimResponse = await client.PostAsJsonAsync(
            $"/reservations/{reservation.ReservationToken}/claim",
            claimRequest);
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var claimedReservation = await claimResponse.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal(ReservationStatus.Claimed, claimedReservation!.Status);
        Assert.Equal("game-server-1", claimedReservation.ServerId);

        // Step 3: Release reservation (e.g., when server is stopped)
        var releaseResponse = await client.DeleteAsync(
            $"/reservations/{reservation.ReservationToken}?reason=server+stopped");
        Assert.Equal(HttpStatusCode.NoContent, releaseResponse.StatusCode);

        // Verify it's released
        var getResponse = await client.GetAsync(
            $"/reservations/{reservation.ReservationToken}");
        var releasedReservation = await getResponse.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal(ReservationStatus.Released, releasedReservation!.Status);
    }
}
