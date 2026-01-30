using System.Net;
using System.Net.Http.Json;
using Dhadgar.Nodes.Models;
using Xunit;

namespace Dhadgar.Nodes.Tests.Integration;

[Collection("Nodes Integration")]
public sealed class EnrollmentApiIntegrationTests
{
    private readonly NodesWebApplicationFactory _factory;
    private static readonly Guid TestUserId = Guid.NewGuid();

    public EnrollmentApiIntegrationTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateToken_WithoutAuth_ReturnsUnauthorized()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var request = new CreateEnrollmentTokenRequest("Test Token", null);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateToken_WithAuth_ReturnsCreated()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var request = new CreateEnrollmentTokenRequest("Test Token", null);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateEnrollmentTokenResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.TokenId);
        Assert.NotEmpty(result.Token);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateToken_WithCustomExpiry_ReturnsCorrectExpiry()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var request = new CreateEnrollmentTokenRequest("Custom Expiry Token", 120); // 2 hours
        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateEnrollmentTokenResponse>();
        Assert.NotNull(result);

        // Should be approximately 2 hours from now (allow some buffer)
        var expectedExpiry = _factory.TimeProvider.GetUtcNow().UtcDateTime.AddMinutes(120);
        Assert.True((result.ExpiresAt - expectedExpiry).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ListTokens_WithoutAuth_ReturnsUnauthorized()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/enrollment/tokens");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListTokens_EmptyOrganization_ReturnsEmptyList()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/enrollment/tokens");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<EnrollmentTokenSummary>>();
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListTokens_ReturnsActiveTokensOnly()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create a token
        var createRequest = new CreateEnrollmentTokenRequest("Active Token", null);
        await client.PostAsJsonAsync($"/api/v1/organizations/{orgId}/enrollment/tokens", createRequest);

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/enrollment/tokens");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<EnrollmentTokenSummary>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Active Token", result[0].Label);
    }

    [Fact]
    public async Task RevokeToken_RevokesSuccessfully()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create a token
        var createRequest = new CreateEnrollmentTokenRequest("To Revoke", null);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateEnrollmentTokenResponse>();

        // Revoke the token
        var revokeResponse = await client.DeleteAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens/{created!.TokenId}");

        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        // Verify token no longer appears in active list
        var listResponse = await client.GetAsync($"/api/v1/organizations/{orgId}/enrollment/tokens");
        var tokens = await listResponse.Content.ReadFromJsonAsync<List<EnrollmentTokenSummary>>();
        Assert.NotNull(tokens);
        Assert.DoesNotContain(tokens, t => t.Id == created.TokenId);
    }

    [Fact]
    public async Task RevokeToken_RevokedTokenCannotBeUsedForEnrollment()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create a token
        var createRequest = new CreateEnrollmentTokenRequest("To Revoke", null);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateEnrollmentTokenResponse>();

        // Revoke the token
        await client.DeleteAsync($"/api/v1/organizations/{orgId}/enrollment/tokens/{created!.TokenId}");

        // Try to use the revoked token for enrollment
        using var anonClient = _factory.CreateClient();
        var enrollRequest = new EnrollNodeRequest(
            Token: created.Token,
            Platform: "linux",
            Hardware: new HardwareInventoryDto(
                Hostname: "test-server",
                OsVersion: "Ubuntu 22.04",
                CpuCores: 8,
                MemoryBytes: 16L * 1024 * 1024 * 1024,
                DiskBytes: 500L * 1024 * 1024 * 1024,
                NetworkInterfaces: null));

        var enrollResponse = await anonClient.PostAsJsonAsync("/api/v1/agents/enroll", enrollRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, enrollResponse.StatusCode);
    }

    [Fact]
    public async Task ListTokens_OrdersByCreatedAtDescending()
    {
        var orgId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(TestUserId, orgId);

        // Create tokens in order
        await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens",
            new CreateEnrollmentTokenRequest("First", null));

        _factory.TimeProvider.Advance(TimeSpan.FromMinutes(1));

        await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens",
            new CreateEnrollmentTokenRequest("Second", null));

        _factory.TimeProvider.Advance(TimeSpan.FromMinutes(1));

        await client.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId}/enrollment/tokens",
            new CreateEnrollmentTokenRequest("Third", null));

        var response = await client.GetAsync($"/api/v1/organizations/{orgId}/enrollment/tokens");
        var tokens = await response.Content.ReadFromJsonAsync<List<EnrollmentTokenSummary>>();

        Assert.NotNull(tokens);
        Assert.Equal(3, tokens.Count);
        Assert.Equal("Third", tokens[0].Label);
        Assert.Equal("Second", tokens[1].Label);
        Assert.Equal("First", tokens[2].Label);
    }

    [Fact]
    public async Task ListTokens_DifferentOrganizations_AreIsolated()
    {
        var orgId1 = Guid.NewGuid();
        var orgId2 = Guid.NewGuid();

        // Create token in org1
        using var client1 = _factory.CreateAuthenticatedClient(TestUserId, orgId1);
        await client1.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId1}/enrollment/tokens",
            new CreateEnrollmentTokenRequest("Org1 Token", null));

        // Create token in org2
        using var client2 = _factory.CreateAuthenticatedClient(TestUserId, orgId2);
        await client2.PostAsJsonAsync(
            $"/api/v1/organizations/{orgId2}/enrollment/tokens",
            new CreateEnrollmentTokenRequest("Org2 Token", null));

        // List tokens for org1 - should only see org1's token
        var response1 = await client1.GetAsync($"/api/v1/organizations/{orgId1}/enrollment/tokens");
        var tokens1 = await response1.Content.ReadFromJsonAsync<List<EnrollmentTokenSummary>>();
        Assert.NotNull(tokens1);
        Assert.Single(tokens1);
        Assert.Equal("Org1 Token", tokens1[0].Label);

        // List tokens for org2 - should only see org2's token
        var response2 = await client2.GetAsync($"/api/v1/organizations/{orgId2}/enrollment/tokens");
        var tokens2 = await response2.Content.ReadFromJsonAsync<List<EnrollmentTokenSummary>>();
        Assert.NotNull(tokens2);
        Assert.Single(tokens2);
        Assert.Equal("Org2 Token", tokens2[0].Label);
    }

    [Fact]
    public async Task CreateToken_CrossTenantAccess_ReturnsForbidden()
    {
        var userOrgId = Guid.NewGuid();
        var targetOrgId = Guid.NewGuid();

        // Client authenticated to userOrgId tries to create token in targetOrgId
        using var client = _factory.CreateAuthenticatedClient(TestUserId, userOrgId);

        var request = new CreateEnrollmentTokenRequest("Cross-tenant Token", null);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{targetOrgId}/enrollment/tokens", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
