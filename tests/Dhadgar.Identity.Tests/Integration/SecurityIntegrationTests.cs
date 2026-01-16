using System.Net;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

/// <summary>
/// Integration tests for security fixes including header trust vulnerability.
/// </summary>
public sealed class SecurityIntegrationTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SecurityIntegrationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithOldXUserIdHeaderOnly_ReturnsUnauthorized()
    {
        // Arrange: Seed user with organization
        var (userId, orgId) = await SeedUserWithOrganizationAsync("oldheader@example.com");

        // Use the old X-User-Id header (which should NOT be trusted anymore)
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/organizations/{orgId}/users");
        request.Headers.Add("X-User-Id", userId.ToString());  // Old untrusted header

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Should be unauthorized because X-User-Id is not trusted
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidTestAuth_ReturnsSuccess()
    {
        // Arrange: Seed user with organization and proper permissions
        var (userId, orgId) = await SeedUserWithOrganizationAsync("validauth@example.com", "admin");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/organizations/{orgId}/users");
        IdentityWebApplicationFactory.AddTestAuth(request, userId, orgId, "admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Should succeed with proper authentication
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithNoAuthentication_ReturnsUnauthorized()
    {
        // Arrange: Seed user with organization to have a valid org ID
        var (_, orgId) = await SeedUserWithOrganizationAsync("noauth@example.com");

        // No authentication headers at all
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/organizations/{orgId}/users");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Should be unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedUserId_ReturnsUnauthorized()
    {
        // Arrange: Seed user with organization to have a valid org ID
        var (_, orgId) = await SeedUserWithOrganizationAsync("malformed@example.com");

        // Try with malformed user ID in test auth header
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/organizations/{orgId}/users");
        request.Headers.Add(TestAuthHandler.UserIdHeader, "not-a-valid-guid");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Should be unauthorized due to invalid GUID
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousEndpoint_WithNoAuthentication_ReturnsSuccess()
    {
        // Arrange: Request to anonymous endpoint
        using var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Anonymous endpoints should work without auth
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Note: Health endpoint test removed - requires IConnectionMultiplexer which isn't mocked in test factory.
    // Health checks are tested separately in ReadinessIntegrationTests.

    [Fact]
    public async Task OAuthLinkEndpoint_WithOldXUserIdHeaderOnly_ReturnsUnauthorized()
    {
        // Arrange: OAuth link endpoint with old header
        var (userId, _) = await SeedUserWithOrganizationAsync("oauthlink@example.com");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/oauth/steam/link");
        request.Headers.Add("X-User-Id", userId.ToString());  // Old untrusted header

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Should be unauthorized because X-User-Id is not trusted
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTestAuthButWrongOrg_ReturnsForbidden()
    {
        // Arrange: Create user with one org, but try to access another org's data
        var (userId, orgId) = await SeedUserWithOrganizationAsync("wrongorg@example.com", "admin");

        // Create a different organization that the user doesn't belong to
        var otherOrgId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Organizations.Add(new Organization
        {
            Id = otherOrgId,
            Name = "Other Org",
            Slug = $"other-org-{Guid.NewGuid():N}",
            Settings = new OrganizationSettings()
        });
        await db.SaveChangesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/organizations/{otherOrgId}/users");
        IdentityWebApplicationFactory.AddTestAuth(request, userId, orgId, "admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Should be forbidden because user doesn't have membership in that org
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(Guid UserId, Guid OrgId)> SeedUserWithOrganizationAsync(
        string email,
        string role = "viewer")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = email,
            EmailVerified = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = $"test-org-{Guid.NewGuid():N}",
            Settings = new OrganizationSettings()
        };
        db.Organizations.Add(org);

        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            Role = role,
            IsActive = true
        });

        await db.SaveChangesAsync();

        return (user.Id, org.Id);
    }
}
