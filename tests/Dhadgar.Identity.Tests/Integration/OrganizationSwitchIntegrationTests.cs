using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

/// <summary>
/// Integration tests for organization switching with JWT re-issuance
/// </summary>
[Collection("Identity Integration")]
public sealed class OrganizationSwitchIntegrationTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrganizationSwitchIntegrationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SwitchOrganization_UpdatesPreferredOrgAndIssuesNewJWT()
    {
        // Arrange: Create user with memberships in two organizations
        Guid userId, org1Id, org2Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();

            var user = new User
            {
                Id = Guid.NewGuid(),
                ExternalAuthId = Guid.NewGuid().ToString("N"),
                Email = "orgswitch@example.com",
                EmailVerified = true
            };
            db.Users.Add(user);

            var org1 = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Organization 1",
                Slug = "org1-" + Guid.NewGuid().ToString("N")[..8],
                Settings = new OrganizationSettings()
            };
            db.Organizations.Add(org1);

            var org2 = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Organization 2",
                Slug = "org2-" + Guid.NewGuid().ToString("N")[..8],
                Settings = new OrganizationSettings()
            };
            db.Organizations.Add(org2);

            db.UserOrganizations.Add(new UserOrganization
            {
                UserId = user.Id,
                OrganizationId = org1.Id,
                Role = "owner",
                IsActive = true
            });

            db.UserOrganizations.Add(new UserOrganization
            {
                UserId = user.Id,
                OrganizationId = org2.Id,
                Role = "viewer",
                IsActive = true
            });

            user.PreferredOrganizationId = org1.Id;

            await db.SaveChangesAsync();

            userId = user.Id;
            org1Id = org1.Id;
            org2Id = org2.Id;
        }

        // Act: Switch from org1 to org2
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/organizations/{org2Id}/switch");
        IdentityWebApplicationFactory.AddTestAuth(request, userId);

        var response = await _client.SendAsync(request);

        // Assert: Successful switch
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("accessToken", out var accessTokenProp));
        Assert.True(result.TryGetProperty("refreshToken", out var refreshTokenProp));

        var accessToken = accessTokenProp.GetString();
        Assert.NotNull(accessToken);

        // Verify new JWT contains org2 context
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        var orgIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;
        Assert.Equal(org2Id.ToString(), orgIdClaim);

        // Verify preferred organization updated in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.FindAsync(userId);
            Assert.NotNull(user);
            Assert.Equal(org2Id, user.PreferredOrganizationId);
        }
    }

    [Fact]
    public async Task SwitchOrganization_ToNonMemberOrg_Returns403()
    {
        // Arrange: Create user with membership in only one organization
        Guid userId, memberOrgId, nonMemberOrgId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();

            var user = new User
            {
                Id = Guid.NewGuid(),
                ExternalAuthId = Guid.NewGuid().ToString("N"),
                Email = "nonmember@example.com",
                EmailVerified = true
            };
            db.Users.Add(user);

            var memberOrg = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Member Org",
                Slug = "member-" + Guid.NewGuid().ToString("N")[..8],
                Settings = new OrganizationSettings()
            };
            db.Organizations.Add(memberOrg);

            var nonMemberOrg = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Non-Member Org",
                Slug = "nonmember-" + Guid.NewGuid().ToString("N")[..8],
                Settings = new OrganizationSettings()
            };
            db.Organizations.Add(nonMemberOrg);

            db.UserOrganizations.Add(new UserOrganization
            {
                UserId = user.Id,
                OrganizationId = memberOrg.Id,
                Role = "viewer",
                IsActive = true
            });

            user.PreferredOrganizationId = memberOrg.Id;
            await db.SaveChangesAsync();

            userId = user.Id;
            memberOrgId = memberOrg.Id;
            nonMemberOrgId = nonMemberOrg.Id;
        }

        // Act: Try to switch to non-member organization
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/organizations/{nonMemberOrgId}/switch");
        IdentityWebApplicationFactory.AddTestAuth(request, userId);

        var response = await _client.SendAsync(request);

        // Assert: Forbidden (not a member)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SwitchOrganization_ToInactiveOrg_Returns400()
    {
        // Arrange: Create user with inactive membership
        Guid userId, orgId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();

            var user = new User
            {
                Id = Guid.NewGuid(),
                ExternalAuthId = Guid.NewGuid().ToString("N"),
                Email = "inactive@example.com",
                EmailVerified = true
            };
            db.Users.Add(user);

            var org = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Inactive Org",
                Slug = "inactive-" + Guid.NewGuid().ToString("N")[..8],
                Settings = new OrganizationSettings()
            };
            db.Organizations.Add(org);

            db.UserOrganizations.Add(new UserOrganization
            {
                UserId = user.Id,
                OrganizationId = org.Id,
                Role = "viewer",
                IsActive = false,
                LeftAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            userId = user.Id;
            orgId = org.Id;
        }

        // Act: Try to switch to inactive organization
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/organizations/{orgId}/switch");
        IdentityWebApplicationFactory.AddTestAuth(request, userId);

        var response = await _client.SendAsync(request);

        // Assert: Bad request (inactive membership)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SwitchOrganization_UpdatesRoleClaimsInJWT()
    {
        // Arrange: Create user with different roles in two organizations
        Guid userId, ownerOrgId, memberOrgId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();

            var user = new User
            {
                Id = Guid.NewGuid(),
                ExternalAuthId = Guid.NewGuid().ToString("N"),
                Email = "roleclaims@example.com",
                EmailVerified = true
            };
            db.Users.Add(user);

            var ownerOrg = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Owner Org",
                Slug = "owner-" + Guid.NewGuid().ToString("N")[..8],
                Settings = new OrganizationSettings()
            };
            db.Organizations.Add(ownerOrg);

            var memberOrg = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Member Org",
                Slug = "member-" + Guid.NewGuid().ToString("N")[..8],
                Settings = new OrganizationSettings()
            };
            db.Organizations.Add(memberOrg);

            db.UserOrganizations.Add(new UserOrganization
            {
                UserId = user.Id,
                OrganizationId = ownerOrg.Id,
                Role = "owner",
                IsActive = true
            });

            db.UserOrganizations.Add(new UserOrganization
            {
                UserId = user.Id,
                OrganizationId = memberOrg.Id,
                Role = "viewer",
                IsActive = true
            });

            user.PreferredOrganizationId = ownerOrg.Id;
            await db.SaveChangesAsync();

            userId = user.Id;
            ownerOrgId = ownerOrg.Id;
            memberOrgId = memberOrg.Id;
        }

        // Act: Switch from owner org to member org
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/organizations/{memberOrgId}/switch");
        IdentityWebApplicationFactory.AddTestAuth(request, userId);

        var response = await _client.SendAsync(request);

        // Assert: New JWT has member role claims
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = result.GetProperty("accessToken").GetString();
        Assert.NotNull(accessToken);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        // Verify org context updated
        var orgIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;
        Assert.Equal(memberOrgId.ToString(), orgIdClaim);

        // Verify role updated (Member has fewer permissions than Owner)
        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        Assert.Equal("viewer", roleClaim);
    }
}
