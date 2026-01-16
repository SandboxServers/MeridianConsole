using System.Net;
using System.Net.Http.Json;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

public sealed class BulkEndpointTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;

    public BulkEndpointTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BulkInvite_invites_multiple_users()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var ownerId = Guid.NewGuid();
        var invitee1Id = Guid.NewGuid();
        var invitee2Id = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var owner = new User
        {
            Id = ownerId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "bulkowner@example.com",
            EmailVerified = true
        };
        var invitee1 = new User
        {
            Id = invitee1Id,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "bulkinvitee1@example.com",
            EmailVerified = true
        };
        var invitee2 = new User
        {
            Id = invitee2Id,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "bulkinvitee2@example.com",
            EmailVerified = true
        };

        db.Users.AddRange(owner, invitee1, invitee2);

        var org = new Organization
        {
            Id = orgId,
            Name = "Bulk Org",
            Slug = $"bulk-org-{Guid.NewGuid():N}",
            OwnerId = ownerId
        };
        db.Organizations.Add(org);

        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = ownerId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        });

        // Add permissions
        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "members:invite", Category = "members" });
        await db.SaveChangesAsync();

        var membership = db.UserOrganizations.First(uo => uo.UserId == ownerId && uo.OrganizationId == orgId);
        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "members:invite",
            GrantedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(ownerId, orgId, "owner");
        var request = new
        {
            Invites = new[]
            {
                new { UserId = invitee1Id, Role = "viewer" },
                new { UserId = invitee2Id, Role = "operator" }
            }
        };

        var response = await client.PostAsJsonAsync($"/organizations/{orgId}/members/bulk-invite", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<BulkOperationResponse>();
        Assert.NotNull(content);
        Assert.Equal(2, content.SuccessCount);
        Assert.Equal(0, content.FailCount);
    }

    [Fact]
    public async Task BulkInvite_returns_partial_success()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var ownerId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var owner = new User
        {
            Id = ownerId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "partialowner@example.com",
            EmailVerified = true
        };
        var invitee = new User
        {
            Id = inviteeId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "partialinvitee@example.com",
            EmailVerified = true
        };

        db.Users.AddRange(owner, invitee);

        var org = new Organization
        {
            Id = orgId,
            Name = "Partial Org",
            Slug = $"partial-org-{Guid.NewGuid():N}",
            OwnerId = ownerId
        };
        db.Organizations.Add(org);

        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = ownerId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        });

        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "members:invite", Category = "members" });
        await db.SaveChangesAsync();

        var membership = db.UserOrganizations.First(uo => uo.UserId == ownerId && uo.OrganizationId == orgId);
        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "members:invite",
            GrantedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(ownerId, orgId, "owner");
        var request = new
        {
            Invites = new[]
            {
                new { UserId = inviteeId, Role = "viewer" },
                new { UserId = Guid.NewGuid(), Role = "viewer" } // Non-existent user
            }
        };

        var response = await client.PostAsJsonAsync($"/organizations/{orgId}/members/bulk-invite", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<BulkOperationResponse>();
        Assert.NotNull(content);
        Assert.Equal(1, content.SuccessCount);
        Assert.Equal(1, content.FailCount);
    }

    [Fact]
    public async Task BulkInvite_requires_members_invite_permission()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "noperm@example.com",
            EmailVerified = true
        };

        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "No Perm Org",
            Slug = $"noperm-bulk-org-{Guid.NewGuid():N}",
            OwnerId = userId
        };
        db.Organizations.Add(org);

        // Viewer without invite permission
        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = userId,
            OrganizationId = orgId,
            Role = "viewer",
            IsActive = true
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId, "viewer");
        var request = new
        {
            Invites = new[] { new { UserId = Guid.NewGuid(), Role = "viewer" } }
        };

        var response = await client.PostAsJsonAsync($"/organizations/{orgId}/members/bulk-invite", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BulkRemove_removes_multiple_members()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var ownerId = Guid.NewGuid();
        var member1Id = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var owner = new User
        {
            Id = ownerId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "removeowner@example.com",
            EmailVerified = true
        };
        var member1 = new User
        {
            Id = member1Id,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "removemember1@example.com",
            EmailVerified = true
        };
        var member2 = new User
        {
            Id = member2Id,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "removemember2@example.com",
            EmailVerified = true
        };

        db.Users.AddRange(owner, member1, member2);

        var org = new Organization
        {
            Id = orgId,
            Name = "Remove Org",
            Slug = $"remove-org-{Guid.NewGuid():N}",
            OwnerId = ownerId
        };
        db.Organizations.Add(org);

        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = ownerId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        });
        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = member1Id,
            OrganizationId = orgId,
            Role = "viewer",
            IsActive = true
        });
        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = member2Id,
            OrganizationId = orgId,
            Role = "viewer",
            IsActive = true
        });

        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "members:remove", Category = "members" });
        await db.SaveChangesAsync();

        var membership = db.UserOrganizations.First(uo => uo.UserId == ownerId && uo.OrganizationId == orgId);
        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "members:remove",
            GrantedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(ownerId, orgId, "owner");
        var request = new
        {
            MemberIds = new[] { member1Id, member2Id }
        };

        var response = await client.PostAsJsonAsync($"/organizations/{orgId}/members/bulk-remove", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<BulkOperationResponse>();
        Assert.NotNull(content);
        Assert.Equal(2, content.SuccessCount);
        Assert.Equal(0, content.FailCount);
    }

    [Fact]
    public async Task BulkInvite_rejects_empty_request()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "emptyreq@example.com",
            EmailVerified = true
        };

        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "Empty Req Org",
            Slug = $"emptyreq-org-{Guid.NewGuid():N}",
            OwnerId = userId
        };
        db.Organizations.Add(org);

        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = userId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        });

        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "members:invite", Category = "members" });
        await db.SaveChangesAsync();

        var membership = db.UserOrganizations.First(uo => uo.UserId == userId && uo.OrganizationId == orgId);
        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "members:invite",
            GrantedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId, "owner");
        var request = new
        {
            Invites = Array.Empty<object>()
        };

        var response = await client.PostAsJsonAsync($"/organizations/{orgId}/members/bulk-invite", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record BulkOperationResponse(
        IReadOnlyCollection<Guid> Succeeded,
        IReadOnlyCollection<object> Failed,
        int TotalRequested,
        int SuccessCount,
        int FailCount);
}
