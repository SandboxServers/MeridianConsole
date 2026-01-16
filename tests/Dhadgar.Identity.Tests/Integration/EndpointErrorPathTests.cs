using System.Net;
using System.Net.Http.Json;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

public sealed class EndpointErrorPathTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;

    public EndpointErrorPathTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOrganization_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/organizations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganization_returns_403_without_permission()
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
            Email = "noaccess@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "No Access Org",
            Slug = $"noaccess-org-{Guid.NewGuid():N}",
            OwnerId = Guid.NewGuid() // Different owner
        };
        db.Organizations.Add(org);

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/organizations/{orgId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListMembers_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/organizations/{Guid.NewGuid()}/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InviteMember_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/organizations/{Guid.NewGuid()}/members/invite",
            new { UserId = Guid.NewGuid(), Role = "viewer" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TransferOwnership_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/organizations/{Guid.NewGuid()}/transfer-ownership",
            new { NewOwnerId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TransferOwnership_returns_403_for_non_owner()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var owner = new User
        {
            Id = ownerId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "actualowner@example.com",
            EmailVerified = true
        };
        var admin = new User
        {
            Id = adminId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "admin@example.com",
            EmailVerified = true
        };
        var newOwner = new User
        {
            Id = newOwnerId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "newowner@example.com",
            EmailVerified = true
        };

        db.Users.AddRange(owner, admin, newOwner);

        var org = new Organization
        {
            Id = orgId,
            Name = "Transfer Test Org",
            Slug = $"transfer-org-{Guid.NewGuid():N}",
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
            UserId = adminId,
            OrganizationId = orgId,
            Role = "admin",
            IsActive = true
        });
        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = newOwnerId,
            OrganizationId = orgId,
            Role = "viewer",
            IsActive = true
        });

        await db.SaveChangesAsync();

        // Admin tries to transfer ownership
        var client = _factory.CreateAuthenticatedClient(adminId, orgId, "admin");
        var response = await client.PostAsJsonAsync(
            $"/organizations/{orgId}/transfer-ownership",
            new { NewOwnerId = newOwnerId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyInvitations_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/me/invitations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvite_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/organizations/{Guid.NewGuid()}/members/accept",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectInvite_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/organizations/{Guid.NewGuid()}/members/reject",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithdrawInvitation_returns_401_without_auth()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync(
            $"/organizations/{Guid.NewGuid()}/invitations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMfaPolicy_returns_501_not_implemented()
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
            Email = $"mfauser-{Guid.NewGuid():N}@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "MFA Org",
            Slug = $"mfa-org-{Guid.NewGuid():N}",
            OwnerId = userId
        };
        db.Organizations.Add(org);

        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "org:security", Category = "organization" });

        var membership = new UserOrganization
        {
            UserId = userId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        };
        db.UserOrganizations.Add(membership);
        await db.SaveChangesAsync();

        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "org:security",
            GrantedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId, "owner");
        var response = await client.GetAsync($"/organizations/{orgId}/security/mfa");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task SetMfaPolicy_returns_501_not_implemented()
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
            Email = $"mfasetuser-{Guid.NewGuid():N}@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "MFA Set Org",
            Slug = $"mfaset-org-{Guid.NewGuid():N}",
            OwnerId = userId
        };
        db.Organizations.Add(org);

        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "org:security", Category = "organization" });

        var membership = new UserOrganization
        {
            UserId = userId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        };
        db.UserOrganizations.Add(membership);
        await db.SaveChangesAsync();

        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "org:security",
            GrantedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId, "owner");
        var response = await client.PutAsJsonAsync(
            $"/organizations/{orgId}/security/mfa",
            new { Required = true });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task CancelDeletion_returns_400_when_no_pending_deletion()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "nodeleteuser@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId);
        var response = await client.PostAsync("/me/cancel-deletion", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
