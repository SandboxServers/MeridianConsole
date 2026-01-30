using System.Net;
using System.Net.Http.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Alias local models to avoid ambiguity with Contracts types
using EnrollNodeRequest = Dhadgar.Nodes.Models.EnrollNodeRequest;
using EnrollNodeResponse = Dhadgar.Nodes.Models.EnrollNodeResponse;
using HardwareInventoryDto = Dhadgar.Nodes.Models.HardwareInventoryDto;

namespace Dhadgar.Nodes.Tests.Integration;

[Collection("Nodes Integration")]
public sealed class AgentApiIntegrationTests
{
    private readonly NodesWebApplicationFactory _factory;
    private static readonly Guid TestOrgId = Guid.NewGuid();
    private const string TestUserId = "user-123";

    public AgentApiIntegrationTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Enroll_WithValidToken_CreatesNode()
    {
        // Create enrollment token first
        var token = await CreateEnrollmentTokenAsync(TestOrgId);
        using var client = _factory.CreateClient();

        var request = new EnrollNodeRequest(
            Token: token,
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "test-server",
                OsVersion: "Ubuntu 22.04",
                CpuCores: 8,
                MemoryBytes: 16L * 1024 * 1024 * 1024,
                DiskBytes: 500L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        var response = await client.PostAsJsonAsync("/api/v1/agents/enroll", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EnrollNodeResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.NodeId);
        Assert.NotEmpty(result.CertificateThumbprint);
        Assert.Contains("BEGIN CERTIFICATE", result.Certificate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enroll_PublishesNodeEnrolledEvent()
    {
        var orgId = Guid.NewGuid();
        var token = await CreateEnrollmentTokenAsync(orgId);
        _factory.EventPublisher.Clear();
        using var client = _factory.CreateClient();

        var request = new EnrollNodeRequest(
            Token: token,
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "event-test-server",
                OsVersion: "Ubuntu 22.04",
                CpuCores: 4,
                MemoryBytes: 8L * 1024 * 1024 * 1024,
                DiskBytes: 100L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        await client.PostAsJsonAsync("/api/v1/agents/enroll", request);

        Assert.True(_factory.EventPublisher.HasMessage<NodeEnrolled>());
        var evt = _factory.EventPublisher.GetLastMessage<NodeEnrolled>()!;
        Assert.Equal(orgId, evt.OrganizationId);
        Assert.Equal("linux", evt.Platform);
    }

    [Fact]
    public async Task Enroll_WithInvalidToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var request = new EnrollNodeRequest(
            Token: "invalid-token-that-does-not-exist",
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "test-server",
                OsVersion: "Ubuntu 22.04",
                CpuCores: 8,
                MemoryBytes: 16L * 1024 * 1024 * 1024,
                DiskBytes: 500L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        var response = await client.PostAsJsonAsync("/api/v1/agents/enroll", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_WithExpiredToken_ReturnsUnauthorized()
    {
        // Note: This test advances the shared TimeProvider. Other tests in this collection
        // should create tokens/entities relative to current time, not rely on a specific time state.
        var orgId = Guid.NewGuid();
        var token = await CreateEnrollmentTokenAsync(orgId, TimeSpan.FromMinutes(30));

        // Advance time past expiration (token was created with 30-min validity)
        _factory.TimeProvider.Advance(TimeSpan.FromHours(1));

        using var client = _factory.CreateClient();
        var request = new EnrollNodeRequest(
            Token: token,
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "test-server",
                OsVersion: "Ubuntu 22.04",
                CpuCores: 8,
                MemoryBytes: 16L * 1024 * 1024 * 1024,
                DiskBytes: 500L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        var response = await client.PostAsJsonAsync("/api/v1/agents/enroll", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_WithInvalidPlatform_ReturnsBadRequest()
    {
        var token = await CreateEnrollmentTokenAsync(TestOrgId);
        using var client = _factory.CreateClient();

        var request = new EnrollNodeRequest(
            Token: token,
            Platform: "macos", // Invalid platform
            Hardware: new HardwareInventoryDto(
                Hostname: "test-server",
                OsVersion: "macOS 14",
                CpuCores: 8,
                MemoryBytes: 16L * 1024 * 1024 * 1024,
                DiskBytes: 500L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        var response = await client.PostAsJsonAsync("/api/v1/agents/enroll", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_TokenCanOnlyBeUsedOnce()
    {
        var token = await CreateEnrollmentTokenAsync(TestOrgId);
        using var client = _factory.CreateClient();

        var request = new EnrollNodeRequest(
            Token: token,
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "test-server",
                OsVersion: "Ubuntu 22.04",
                CpuCores: 8,
                MemoryBytes: 16L * 1024 * 1024 * 1024,
                DiskBytes: 500L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        // First enrollment should succeed
        var response1 = await client.PostAsJsonAsync("/api/v1/agents/enroll", request);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Second enrollment with same token should fail
        var response2 = await client.PostAsJsonAsync("/api/v1/agents/enroll", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response2.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WithValidNode_ReturnsOk()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "heartbeat-node");
        using var client = _factory.CreateAgentClient(node.Id);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 2,
            AgentVersion: "1.0.1",
            HealthIssues: null);

        var response = await client.PostAsJsonAsync($"/api/v1/agents/{node.Id}/heartbeat", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>();
        Assert.NotNull(result);
        Assert.True(result.Acknowledged);
    }

    [Fact]
    public async Task Heartbeat_WithoutAuth_ReturnsUnauthorized()
    {
        var nodeId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 2,
            AgentVersion: "1.0.0",
            HealthIssues: null);

        var response = await client.PostAsJsonAsync($"/api/v1/agents/{nodeId}/heartbeat", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_ForNonExistentNode_ReturnsNotFound()
    {
        var fakeNodeId = Guid.NewGuid();
        using var client = _factory.CreateAgentClient(fakeNodeId);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 2,
            AgentVersion: "1.0.0",
            HealthIssues: null);

        var response = await client.PostAsJsonAsync($"/api/v1/agents/{fakeNodeId}/heartbeat", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_ForDecommissionedNode_ReturnsBadRequest()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "decommissioned-node", NodeStatus.Decommissioned);
        using var client = _factory.CreateAgentClient(node.Id);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 0,
            AgentVersion: "1.0.0",
            HealthIssues: null);

        var response = await client.PostAsJsonAsync($"/api/v1/agents/{node.Id}/heartbeat", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_PublishesOnlineEvent_WhenNodeComesOnline()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "offline-node", NodeStatus.Offline);
        _factory.EventPublisher.Clear();
        using var client = _factory.CreateAgentClient(node.Id);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 0,
            AgentVersion: "1.0.0",
            HealthIssues: null);

        await client.PostAsJsonAsync($"/api/v1/agents/{node.Id}/heartbeat", request);

        Assert.True(_factory.EventPublisher.HasMessage<NodeOnline>());
        var evt = _factory.EventPublisher.GetLastMessage<NodeOnline>()!;
        Assert.Equal(node.Id, evt.NodeId);
    }

    [Fact]
    public async Task Heartbeat_PublishesDegradedEvent_WhenHighCpu()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "cpu-stress-node", NodeStatus.Online);
        _factory.EventPublisher.Clear();
        using var client = _factory.CreateAgentClient(node.Id);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 95.0, // Above 90% threshold
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 5,
            AgentVersion: "1.0.0",
            HealthIssues: null);

        await client.PostAsJsonAsync($"/api/v1/agents/{node.Id}/heartbeat", request);

        Assert.True(_factory.EventPublisher.HasMessage<NodeDegraded>());
    }

    /// <summary>
    /// Helper to create an enrollment token for testing.
    /// </summary>
    private async Task<string> CreateEnrollmentTokenAsync(Guid organizationId, TimeSpan? validity = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        await db.Database.EnsureCreatedAsync();

        var tokenService = scope.ServiceProvider.GetRequiredService<IEnrollmentTokenService>();
        var (_, plainTextToken) = await tokenService.CreateTokenAsync(
            organizationId,
            TestUserId,
            "Test Token",
            validity);

        return plainTextToken;
    }
}
