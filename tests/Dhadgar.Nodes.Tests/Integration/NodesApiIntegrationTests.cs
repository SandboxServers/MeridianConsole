using System.Net;
using System.Net.Http.Json;
using Dhadgar.Contracts;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Xunit;

namespace Dhadgar.Nodes.Tests.Integration;

public sealed class NodesApiIntegrationTests : IClassFixture<NodesWebApplicationFactory>
{
    private readonly NodesWebApplicationFactory _factory;
    private static readonly Guid TestOrgId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public NodesApiIntegrationTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LivezEndpoint_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/livez");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyzEndpoint_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RootEndpoint_ReturnsServiceInfo()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Dhadgar.Nodes", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListNodes_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var orgId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListNodes_WithAuth_ReturnsOk()
    {
        // Ensure database is created (no need to seed, just initialize)
        await _factory.EnsureDatabaseCreatedAsync();

        using var client = _factory.CreateAuthenticatedClient(TestUserId, TestOrgId);

        var response = await client.GetAsync($"/api/v1/organizations/{TestOrgId}/nodes");

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Expected OK but got {response.StatusCode}. Body: '{body}'");
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListNodes_EmptyOrganization_ReturnsEmptyList()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FilteredPagedResponse<NodeListItem>>();
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task ListNodes_WithNodes_ReturnsNodes()
    {
        var orgId = Guid.NewGuid();
        var nodes = await _factory.SeedNodesAsync(orgId, 3);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FilteredPagedResponse<NodeListItem>>();
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task ListNodes_SupportsPagination()
    {
        var orgId = Guid.NewGuid();
        await _factory.SeedNodesAsync(orgId, 10);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes?page=1&pageSize=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FilteredPagedResponse<NodeListItem>>();
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(10, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.PageSize);
    }

    [Fact]
    public async Task GetNode_ExistingNode_ReturnsNode()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "my-server");
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes/{node.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<NodeDetail>();
        Assert.NotNull(result);
        Assert.Equal(node.Id, result.Id);
        Assert.Equal("my-server", result.Name);
        Assert.Equal(orgId, result.OrganizationId);
    }

    [Fact]
    public async Task GetNode_NonExistentNode_ReturnsNotFound()
    {
        var orgId = Guid.NewGuid();
        var nonExistentId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes/{nonExistentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetNode_WrongOrganization_ReturnsNotFound()
    {
        var orgId1 = Guid.NewGuid();
        var orgId2 = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId1, "node-in-org1");
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId2);

        // Trying to access node from orgId1 while authenticated to orgId2
        var response = await client.GetAsync($"/api/v1/organizations/{orgId2}/nodes/{node.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateNode_UpdatesNameAndDisplayName()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "original-name");
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var request = new { Name = "updated-name", DisplayName = "Updated Display Name" };
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/organizations/{orgId}/nodes/{node.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<NodeDetail>();
        Assert.NotNull(result);
        Assert.Equal("updated-name", result.Name);
        Assert.Equal("Updated Display Name", result.DisplayName);
    }

    [Fact]
    public async Task UpdateNode_NonExistentNode_ReturnsNotFound()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var request = new { Name = "new-name" };
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/organizations/{orgId}/nodes/{Guid.NewGuid()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DecommissionNode_DecommissionsSuccessfully()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "to-decommission");
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.DeleteAsync($"/api/v1/organizations/{orgId}/nodes/{node.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify node is decommissioned
        var getResponse = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes/{node.Id}");
        // Decommissioned nodes should not be found (soft deleted)
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task EnterMaintenance_PutsNodeInMaintenance()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "maintenance-node", NodeStatus.Online);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.PostAsync(
            $"/api/v1/organizations/{orgId}/nodes/{node.Id}/maintenance", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify node is in maintenance
        var getResponse = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes/{node.Id}");
        var result = await getResponse.Content.ReadFromJsonAsync<NodeDetail>();
        Assert.NotNull(result);
        Assert.Equal(NodeStatus.Maintenance, result.Status);
    }

    [Fact]
    public async Task EnterMaintenance_AlreadyInMaintenance_ReturnsBadRequest()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "already-maintenance", NodeStatus.Maintenance);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.PostAsync(
            $"/api/v1/organizations/{orgId}/nodes/{node.Id}/maintenance", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExitMaintenance_TakesNodeOutOfMaintenance()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "exit-maintenance", NodeStatus.Maintenance);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.DeleteAsync(
            $"/api/v1/organizations/{orgId}/nodes/{node.Id}/maintenance");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify node is out of maintenance
        var getResponse = await client.GetAsync($"/api/v1/organizations/{orgId}/nodes/{node.Id}");
        var result = await getResponse.Content.ReadFromJsonAsync<NodeDetail>();
        Assert.NotNull(result);
        Assert.NotEqual(NodeStatus.Maintenance, result.Status);
    }

    [Fact]
    public async Task ExitMaintenance_NotInMaintenance_ReturnsBadRequest()
    {
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId, "not-in-maintenance", NodeStatus.Online);
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.DeleteAsync(
            $"/api/v1/organizations/{orgId}/nodes/{node.Id}/maintenance");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResponseIncludesCorrelationHeaders()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.True(response.Headers.Contains("X-Request-Id"));
    }

    [Fact]
    public async Task ListNodes_CrossTenantAccess_ReturnsForbidden()
    {
        var userOrgId = Guid.NewGuid();
        var targetOrgId = Guid.NewGuid(); // Different org

        // Client authenticated to userOrgId tries to access targetOrgId
        using var client = _factory.CreateAuthenticatedClient(TestUserId, userOrgId);

        var response = await client.GetAsync($"/api/v1/organizations/{targetOrgId}/nodes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetNode_CrossTenantAccess_ReturnsForbidden()
    {
        var userOrgId = Guid.NewGuid();
        var targetOrgId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        // Client authenticated to userOrgId tries to access node in targetOrgId
        using var client = _factory.CreateAuthenticatedClient(TestUserId, userOrgId);

        var response = await client.GetAsync($"/api/v1/organizations/{targetOrgId}/nodes/{nodeId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
