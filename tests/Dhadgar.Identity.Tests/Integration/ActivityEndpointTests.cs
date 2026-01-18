using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

public sealed class ActivityEndpointTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ActivityEndpointTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMyActivity_returns_user_audit_events()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = $"activityuser-{Guid.NewGuid():N}@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        // Add some audit events for this user
        db.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            UserId = userId,
            OccurredAtUtc = DateTime.UtcNow
        });
        db.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.TokenRefreshed,
            UserId = userId,
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync("/me/activity");

        // Debug: Check response status and content
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {response.StatusCode}: {responseBody}");

        var content = JsonSerializer.Deserialize<ActivityResponse>(responseBody, JsonOptions);
        Assert.NotNull(content);
        Assert.NotNull(content.Events);
        Assert.Equal(2, content.Events.Count);
    }

    [Fact]
    public async Task GetMyActivity_requires_authentication()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/me/activity");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrgActivity_returns_organization_audit_events()
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
            Email = $"orgactivity-{Guid.NewGuid():N}@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "Activity Org",
            Slug = $"activity-org-{Guid.NewGuid():N}",
            OwnerId = userId
        };
        db.Organizations.Add(org);

        var membership = new UserOrganization
        {
            UserId = userId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        };
        db.UserOrganizations.Add(membership);

        // Add org-specific audit events
        db.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.MembershipInvited,
            OrganizationId = orgId,
            OccurredAtUtc = DateTime.UtcNow
        });
        db.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.MembershipAccepted,
            OrganizationId = orgId,
            OccurredAtUtc = DateTime.UtcNow
        });

        // Add claim definition for permission check
        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "org:audit", Category = "organization" });

        await db.SaveChangesAsync();

        // Add claim to user for permission
        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "org:audit",
            GrantedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId, "owner");
        var response = await client.GetAsync($"/organizations/{orgId}/activity");

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {response.StatusCode}: {responseBody}");

        var content = JsonSerializer.Deserialize<ActivityResponse>(responseBody, JsonOptions);
        Assert.NotNull(content);
        Assert.NotNull(content.Events);
        Assert.Equal(2, content.Events.Count);
    }

    [Fact]
    public async Task GetOrgActivity_requires_org_audit_permission()
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
            Email = "nopermission@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "No Perm Org",
            Slug = $"noperm-org-{Guid.NewGuid():N}",
            OwnerId = userId
        };
        db.Organizations.Add(org);

        // Member without org:audit permission
        db.UserOrganizations.Add(new UserOrganization
        {
            UserId = userId,
            OrganizationId = orgId,
            Role = "viewer",
            IsActive = true
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId, "viewer");
        var response = await client.GetAsync($"/organizations/{orgId}/activity");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyActivity_respects_pagination()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = $"paginateduser-{Guid.NewGuid():N}@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        // Add many audit events
        for (var i = 0; i < 15; i++)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                EventType = AuditEventTypes.UserAuthenticated,
                UserId = userId,
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync("/me/activity?skip=0&take=5");

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {response.StatusCode}: {responseBody}");

        var content = JsonSerializer.Deserialize<ActivityResponse>(responseBody, JsonOptions);
        Assert.NotNull(content);
        Assert.NotNull(content.Events);
        Assert.Equal(5, content.Events.Count);
    }

    [Fact]
    public async Task GetOrgActivity_filters_by_event_type()
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
            Email = $"filtertest-{Guid.NewGuid():N}@example.com",
            EmailVerified = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = orgId,
            Name = "Filter Org",
            Slug = $"filter-org-{Guid.NewGuid():N}",
            OwnerId = userId
        };
        db.Organizations.Add(org);

        var membership = new UserOrganization
        {
            UserId = userId,
            OrganizationId = orgId,
            Role = "owner",
            IsActive = true
        };
        db.UserOrganizations.Add(membership);

        // Different event types
        db.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.MembershipInvited,
            OrganizationId = orgId,
            OccurredAtUtc = DateTime.UtcNow
        });
        db.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.MembershipAccepted,
            OrganizationId = orgId,
            OccurredAtUtc = DateTime.UtcNow
        });
        db.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.MembershipInvited,
            OrganizationId = orgId,
            OccurredAtUtc = DateTime.UtcNow
        });

        // Add claim for permission
        db.ClaimDefinitions.Add(new ClaimDefinition { Name = "org:audit", Category = "organization" });
        await db.SaveChangesAsync();

        db.UserOrganizationClaims.Add(new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "org:audit",
            GrantedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(userId, orgId, "owner");
        var response = await client.GetAsync($"/organizations/{orgId}/activity?eventType={AuditEventTypes.MembershipInvited}");

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {response.StatusCode}: {responseBody}");

        var content = JsonSerializer.Deserialize<ActivityResponse>(responseBody, JsonOptions);
        Assert.NotNull(content);
        Assert.NotNull(content.Events);
        Assert.Equal(2, content.Events.Count);
    }

    private sealed record ActivityResponse(IReadOnlyCollection<AuditEvent>? Events, PaginationInfo? Pagination);
    private sealed record PaginationInfo(int Take, int Skip, int Count);
}
