using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class BulkOperationTests
{
    [Fact]
    public async Task BulkInviteAsync_invites_multiple_users()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee1 = await SeedUserAsync(context, "invitee1@example.com");
        var invitee2 = await SeedUserAsync(context, "invitee2@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var requests = new List<MemberInviteRequest>
        {
            new(invitee1.Id, null, "viewer"),
            new(invitee2.Id, null, "operator")
        };

        var result = await service.BulkInviteAsync(org.Id, owner.Id, requests);

        Assert.Equal(2, result.Succeeded.Count);
        Assert.Empty(result.Failed);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task BulkInviteAsync_returns_partial_success()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var validInvitee = await SeedUserAsync(context, "valid@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var requests = new List<MemberInviteRequest>
        {
            new(validInvitee.Id, null, "viewer"),
            new(Guid.NewGuid(), null, "viewer") // Non-existent user
        };

        var result = await service.BulkInviteAsync(org.Id, owner.Id, requests);

        Assert.Single(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.True(result.PartialSuccess);
        Assert.Equal("user_not_found", result.Failed.First().ErrorCode);
    }

    [Fact]
    public async Task BulkInviteAsync_rejects_more_than_50_invites()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var requests = Enumerable.Range(0, 51)
            .Select(i => new MemberInviteRequest(Guid.NewGuid(), null, "viewer"))
            .ToList();

        var result = await service.BulkInviteAsync(org.Id, owner.Id, requests);

        Assert.Empty(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.Equal("too_many_requests", result.Failed.First().ErrorCode);
    }

    [Fact]
    public async Task BulkInviteAsync_handles_empty_request()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.BulkInviteAsync(org.Id, owner.Id, []);

        Assert.Empty(result.Succeeded);
        Assert.Empty(result.Failed);
        Assert.Equal(0, result.TotalRequested);
    }

    [Fact]
    public async Task BulkInviteAsync_fails_duplicate_invitations()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        // First invitation
        var firstRequest = new List<MemberInviteRequest>
        {
            new(invitee.Id, null, "viewer")
        };
        await service.BulkInviteAsync(org.Id, owner.Id, firstRequest);

        // Second invitation attempt for same user
        var secondRequest = new List<MemberInviteRequest>
        {
            new(invitee.Id, null, "viewer")
        };
        var result = await service.BulkInviteAsync(org.Id, owner.Id, secondRequest);

        Assert.Empty(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.Equal("invitation_exists", result.Failed.First().ErrorCode);
    }

    [Fact]
    public async Task BulkRemoveAsync_removes_multiple_members()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member1 = await SeedUserAsync(context, "member1@example.com");
        var member2 = await SeedUserAsync(context, "member2@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        // Add members
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = member1.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = member2.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.BulkRemoveAsync(org.Id, [member1.Id, member2.Id]);

        Assert.Equal(2, result.Succeeded.Count);
        Assert.Empty(result.Failed);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task BulkRemoveAsync_returns_partial_success()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.BulkRemoveAsync(org.Id, [member.Id, Guid.NewGuid()]);

        Assert.Single(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.True(result.PartialSuccess);
        Assert.Equal("membership_not_found", result.Failed.First().ErrorCode);
    }

    [Fact]
    public async Task BulkRemoveAsync_rejects_more_than_50_removals()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var memberIds = Enumerable.Range(0, 51)
            .Select(_ => Guid.NewGuid())
            .ToList();

        var result = await service.BulkRemoveAsync(org.Id, memberIds);

        Assert.Empty(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.Equal("too_many_requests", result.Failed.First().ErrorCode);
    }

    [Fact]
    public async Task BulkRemoveAsync_handles_empty_request()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.BulkRemoveAsync(org.Id, []);

        Assert.Empty(result.Succeeded);
        Assert.Empty(result.Failed);
        Assert.Equal(0, result.TotalRequested);
    }

    [Fact]
    public async Task BulkRemoveAsync_cannot_remove_owner()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.BulkRemoveAsync(org.Id, [owner.Id]);

        Assert.Empty(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.Equal("cannot_remove_owner", result.Failed.First().ErrorCode);
    }

    [Fact]
    public async Task BulkOperationResult_calculates_properties_correctly()
    {
        var succeeded = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var failed = new List<BulkItemError<Guid>>
        {
            new(Guid.NewGuid(), "error1", null),
            new(Guid.NewGuid(), "error2", "details")
        };

        var result = new BulkOperationResult<Guid>(succeeded, failed);

        Assert.Equal(4, result.TotalRequested);
        Assert.True(result.PartialSuccess);
        Assert.False(result.AllSucceeded);
        Assert.False(result.AllFailed);
    }

    [Fact]
    public void BulkOperationResult_AllSucceeded_is_true_when_no_failures()
    {
        var result = new BulkOperationResult<Guid>(
            [Guid.NewGuid()],
            []);

        Assert.True(result.AllSucceeded);
        Assert.False(result.PartialSuccess);
        Assert.False(result.AllFailed);
    }

    [Fact]
    public void BulkOperationResult_AllFailed_is_true_when_no_successes()
    {
        var result = new BulkOperationResult<Guid>(
            [],
            [new BulkItemError<Guid>(Guid.NewGuid(), "error", null)]);

        Assert.True(result.AllFailed);
        Assert.False(result.PartialSuccess);
        Assert.False(result.AllSucceeded);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityDbContext(options);
    }

    private static async Task<User> SeedUserAsync(IdentityDbContext context, string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = email,
            EmailVerified = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<Organization> SeedOrganizationAsync(
        IdentityDbContext context,
        User owner)
    {
        var org = new Organization
        {
            Name = "Test Org",
            Slug = $"org-{owner.Id:N}",
            OwnerId = owner.Id
        };

        context.Organizations.Add(org);
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = owner.Id,
            OrganizationId = org.Id,
            Role = "owner",
            IsActive = true
        });

        await context.SaveChangesAsync();
        return org;
    }
}
