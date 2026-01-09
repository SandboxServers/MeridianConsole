using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Authorization;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class MembershipServiceTests
{
    [Fact]
    public async Task InviteAsync_creates_pending_membership()
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

        var result = await service.InviteAsync(
            org.Id,
            owner.Id,
            new MemberInviteRequest(invitee.Id, null, "viewer"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.False(result.Value?.IsActive);
        Assert.Equal(owner.Id, result.Value?.InvitedByUserId);
        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var inviteEvent));
        Assert.Equal(MembershipChangeTypes.Invited, inviteEvent.ChangeType);
    }

    [Fact]
    public async Task AcceptInviteAsync_activates_membership()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var pending = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id
        };

        context.UserOrganizations.Add(pending);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);
        var result = await service.AcceptInviteAsync(org.Id, invitee.Id);

        Assert.True(result.Success);
        Assert.True(result.Value?.IsActive);
        Assert.NotNull(result.Value?.InvitationAcceptedAt);
        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var acceptEvent));
        Assert.Equal(MembershipChangeTypes.Accepted, acceptEvent.ChangeType);
    }

    [Fact]
    public async Task AssignRoleAsync_respects_role_matrix()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var operatorUser = await SeedUserAsync(context, "operator@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var operatorMembership = new UserOrganization
        {
            UserId = operatorUser.Id,
            OrganizationId = org.Id,
            Role = "operator",
            IsActive = true
        };

        context.UserOrganizations.Add(operatorMembership);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);
        var result = await service.AssignRoleAsync(org.Id, operatorUser.Id, owner.Id, "admin");

        Assert.False(result.Success);
        Assert.Equal("forbidden_role_assignment", result.Error);
        Assert.Empty(eventPublisher.OrgMembershipChangedEvents);
    }

    [Fact]
    public async Task AddClaimAsync_rejects_unknown_claim()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var membership = new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        };

        context.UserOrganizations.Add(membership);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);
        var result = await service.AddClaimAsync(
            org.Id,
            owner.Id,
            member.Id,
            new MemberClaimRequest(ClaimType.Grant, "invalid:claim", null, null, null));

        Assert.False(result.Success);
        Assert.Equal("unknown_claim", result.Error);
        Assert.Empty(eventPublisher.OrgMembershipChangedEvents);
    }

    [Fact]
    public async Task AssignRoleAsync_publishes_event_on_success()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var memberMembership = new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        };

        context.UserOrganizations.Add(memberMembership);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.AssignRoleAsync(org.Id, owner.Id, member.Id, "operator");

        Assert.True(result.Success);
        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var changeEvent));
        Assert.Equal(MembershipChangeTypes.RoleAssigned, changeEvent.ChangeType);
        Assert.Equal("operator", changeEvent.Role);
    }

    [Fact]
    public async Task AddClaimAsync_publishes_event_on_success()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var membership = new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        };

        context.UserOrganizations.Add(membership);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.AddClaimAsync(
            org.Id,
            owner.Id,
            member.Id,
            new MemberClaimRequest(ClaimType.Grant, "org:read", null, null, null));

        Assert.True(result.Success);
        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var changeEvent));
        Assert.Equal(MembershipChangeTypes.ClaimAdded, changeEvent.ChangeType);
        Assert.Equal("org:read", changeEvent.ClaimValue);
    }

    [Fact]
    public async Task RemoveMemberAsync_marks_member_left_and_publishes_event()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var membership = new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        };

        context.UserOrganizations.Add(membership);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.RemoveMemberAsync(org.Id, member.Id);

        Assert.True(result.Success);
        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var changeEvent));
        Assert.Equal(MembershipChangeTypes.Removed, changeEvent.ChangeType);

        var updated = await context.UserOrganizations.SingleAsync(uo => uo.UserId == member.Id);
        Assert.False(updated.IsActive);
        Assert.NotNull(updated.LeftAt);
    }

    [Fact]
    public async Task RemoveClaimAsync_removes_claim_and_publishes_event()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var membership = new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        };

        context.UserOrganizations.Add(membership);
        await context.SaveChangesAsync();

        var claim = new UserOrganizationClaim
        {
            UserOrganizationId = membership.Id,
            ClaimType = ClaimType.Grant,
            ClaimValue = "org:read",
            GrantedAt = DateTime.UtcNow
        };

        context.UserOrganizationClaims.Add(claim);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.RemoveClaimAsync(org.Id, member.Id, claim.Id);

        Assert.True(result.Success);
        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var changeEvent));
        Assert.Equal(MembershipChangeTypes.ClaimRemoved, changeEvent.ChangeType);
        Assert.Equal("org:read", changeEvent.ClaimValue);
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

    private static async Task<Organization> SeedOrganizationAsync(IdentityDbContext context, User owner)
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
