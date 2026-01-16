using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class AuditServiceTests
{
    [Fact]
    public async Task RecordAsync_persists_audit_event()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var service = new AuditService(context, TimeProvider.System);

        await service.RecordAsync(
            AuditEventTypes.UserCreated,
            userId: Guid.NewGuid(),
            organizationId: Guid.NewGuid(),
            details: new { email = "user@example.com" });

        var events = await context.AuditEvents.ToListAsync();
        Assert.Single(events);
        Assert.Equal(AuditEventTypes.UserCreated, events[0].EventType);
    }

    [Fact]
    public async Task RecordAsync_sets_default_timestamp_when_not_provided()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var service = new AuditService(context, TimeProvider.System);

        var auditEvent = new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            UserId = Guid.NewGuid()
        };

        await service.RecordAsync(auditEvent);

        var saved = await context.AuditEvents.SingleAsync();
        Assert.NotEqual(default, saved.OccurredAtUtc);
    }

    [Fact]
    public async Task RecordAsync_preserves_provided_timestamp()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var service = new AuditService(context, TimeProvider.System);
        var specificTime = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var auditEvent = new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            UserId = Guid.NewGuid(),
            OccurredAtUtc = specificTime
        };

        await service.RecordAsync(auditEvent);

        var saved = await context.AuditEvents.SingleAsync();
        Assert.Equal(specificTime, saved.OccurredAtUtc);
    }

    [Fact]
    public async Task RecordAsync_with_parameters_serializes_details_as_json()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var service = new AuditService(context, TimeProvider.System);

        await service.RecordAsync(
            AuditEventTypes.RoleAssigned,
            userId: Guid.NewGuid(),
            details: new { role = "admin", previousRole = "viewer" });

        var saved = await context.AuditEvents.SingleAsync();
        Assert.Contains("admin", saved.Details);
        Assert.Contains("viewer", saved.Details);
    }

    [Fact]
    public async Task GetUserActivityAsync_returns_events_for_user()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        await SeedAuditEventAsync(context, AuditEventTypes.UserAuthenticated, userId, null);
        await SeedAuditEventAsync(context, AuditEventTypes.RoleAssigned, userId, null);
        await SeedAuditEventAsync(context, AuditEventTypes.UserAuthenticated, otherUserId, null);

        var service = new AuditService(context, TimeProvider.System);
        var result = await service.GetUserActivityAsync(userId);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(userId, e.UserId));
    }

    [Fact]
    public async Task GetUserActivityAsync_includes_events_where_user_is_actor()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await SeedAuditEventAsync(context, AuditEventTypes.RoleAssigned, targetUserId, actorUserId);

        var service = new AuditService(context, TimeProvider.System);
        var result = await service.GetUserActivityAsync(actorUserId);

        Assert.Single(result);
        Assert.Equal(actorUserId, result[0].ActorUserId);
    }

    [Fact]
    public async Task GetUserActivityAsync_filters_by_date_range()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            UserId = userId,
            OccurredAtUtc = now.AddDays(-10)
        });
        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            UserId = userId,
            OccurredAtUtc = now.AddDays(-5)
        });
        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            UserId = userId,
            OccurredAtUtc = now.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var service = new AuditService(context, TimeProvider.System);
        var result = await service.GetUserActivityAsync(
            userId,
            from: now.AddDays(-7),
            to: now.AddDays(-2));

        Assert.Single(result);
    }

    [Fact]
    public async Task GetUserActivityAsync_respects_pagination()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var userId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            context.AuditEvents.Add(new AuditEvent
            {
                EventType = AuditEventTypes.UserAuthenticated,
                UserId = userId,
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await context.SaveChangesAsync();

        var service = new AuditService(context, TimeProvider.System);
        var page1 = await service.GetUserActivityAsync(userId, skip: 0, take: 5);
        var page2 = await service.GetUserActivityAsync(userId, skip: 5, take: 5);

        Assert.Equal(5, page1.Count);
        Assert.Equal(5, page2.Count);
        Assert.NotEqual(page1[0].Id, page2[0].Id);
    }

    [Fact]
    public async Task GetOrganizationActivityAsync_returns_events_for_organization()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var orgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();

        await SeedOrgAuditEventAsync(context, AuditEventTypes.MembershipInvited, orgId);
        await SeedOrgAuditEventAsync(context, AuditEventTypes.MembershipAccepted, orgId);
        await SeedOrgAuditEventAsync(context, AuditEventTypes.MembershipInvited, otherOrgId);

        var service = new AuditService(context, TimeProvider.System);
        var result = await service.GetOrganizationActivityAsync(orgId);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(orgId, e.OrganizationId));
    }

    [Fact]
    public async Task GetOrganizationActivityAsync_filters_by_event_type()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var orgId = Guid.NewGuid();

        await SeedOrgAuditEventAsync(context, AuditEventTypes.MembershipInvited, orgId);
        await SeedOrgAuditEventAsync(context, AuditEventTypes.MembershipAccepted, orgId);
        await SeedOrgAuditEventAsync(context, AuditEventTypes.MembershipRemoved, orgId);

        var service = new AuditService(context, TimeProvider.System);
        var result = await service.GetOrganizationActivityAsync(
            orgId,
            eventType: AuditEventTypes.MembershipInvited);

        Assert.Single(result);
        Assert.Equal(AuditEventTypes.MembershipInvited, result[0].EventType);
    }

    [Fact]
    public async Task GetEventCountAsync_returns_total_count()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        for (var i = 0; i < 15; i++)
        {
            await SeedAuditEventAsync(context, AuditEventTypes.UserAuthenticated, Guid.NewGuid(), null);
        }

        var service = new AuditService(context, TimeProvider.System);
        var count = await service.GetEventCountAsync();

        Assert.Equal(15, count);
    }

    [Fact]
    public async Task GetEventCountAsync_filters_by_date()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;

        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            OccurredAtUtc = now.AddDays(-10)
        });
        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            OccurredAtUtc = now.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var service = new AuditService(context, TimeProvider.System);
        var oldCount = await service.GetEventCountAsync(before: now.AddDays(-5));

        Assert.Equal(1, oldCount);
    }

    [Fact(Skip = "ExecuteDeleteAsync not supported by in-memory provider")]
    public async Task DeleteEventsBeforeAsync_removes_old_events()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;

        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            OccurredAtUtc = now.AddDays(-100)
        });
        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            OccurredAtUtc = now.AddDays(-50)
        });
        context.AuditEvents.Add(new AuditEvent
        {
            EventType = AuditEventTypes.UserAuthenticated,
            OccurredAtUtc = now.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var service = new AuditService(context, TimeProvider.System);
        var deleted = await service.DeleteEventsBeforeAsync(now.AddDays(-30));

        Assert.Equal(2, deleted);
        var remaining = await context.AuditEvents.CountAsync();
        Assert.Equal(1, remaining);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityDbContext(options);
    }

    private static async Task SeedAuditEventAsync(
        IdentityDbContext context,
        string eventType,
        Guid userId,
        Guid? actorUserId)
    {
        context.AuditEvents.Add(new AuditEvent
        {
            EventType = eventType,
            UserId = userId,
            ActorUserId = actorUserId,
            OccurredAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedOrgAuditEventAsync(
        IdentityDbContext context,
        string eventType,
        Guid organizationId)
    {
        context.AuditEvents.Add(new AuditEvent
        {
            EventType = eventType,
            OrganizationId = organizationId,
            OccurredAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}
