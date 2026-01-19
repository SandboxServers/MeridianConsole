using Dhadgar.Contracts.Identity;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class InvitationWorkflowTests : IDisposable
{
    // Use SQLite in-memory for ExecuteUpdateAsync support
    private readonly SqliteConnection _connection;

    public InvitationWorkflowTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Create schema once for all tests using this connection
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task InviteAsync_sets_expiration_date()
    {
        using var context = CreateContext();
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
        Assert.NotNull(result.Value?.InvitationExpiresAt);
        Assert.True(result.Value!.InvitationExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task AcceptInviteAsync_fails_for_expired_invitation()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var expiredInvitation = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(-1) // Expired
        };

        context.UserOrganizations.Add(expiredInvitation);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.AcceptInviteAsync(org.Id, invitee.Id);

        Assert.False(result.Success);
        Assert.Equal("invite_expired", result.Error);
    }

    [Fact]
    public async Task AcceptInviteAsync_clears_expiration_on_success()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var invitation = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        context.UserOrganizations.Add(invitation);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.AcceptInviteAsync(org.Id, invitee.Id);

        Assert.True(result.Success);
        Assert.Null(result.Value?.InvitationExpiresAt);
    }

    [Fact]
    public async Task RejectInviteAsync_marks_invitation_as_left()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var invitation = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        context.UserOrganizations.Add(invitation);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.RejectInviteAsync(org.Id, invitee.Id);

        Assert.True(result.Success);

        var updated = await context.UserOrganizations
            .SingleAsync(uo => uo.UserId == invitee.Id && uo.OrganizationId == org.Id);
        Assert.NotNull(updated.LeftAt);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task RejectInviteAsync_publishes_rejected_event()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var invitation = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id
        };

        context.UserOrganizations.Add(invitation);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        await service.RejectInviteAsync(org.Id, invitee.Id);

        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var evt));
        Assert.Equal(MembershipChangeTypes.Rejected, evt.ChangeType);
        Assert.Equal(invitee.Id, evt.UserId);
    }

    [Fact]
    public async Task RejectInviteAsync_fails_for_expired_invitation()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var invitation = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(-1)
        };

        context.UserOrganizations.Add(invitation);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.RejectInviteAsync(org.Id, invitee.Id);

        Assert.False(result.Success);
        Assert.Equal("invite_expired", result.Error);
    }

    [Fact]
    public async Task WithdrawInviteAsync_marks_invitation_as_left()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var invitation = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id
        };

        context.UserOrganizations.Add(invitation);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.WithdrawInviteAsync(org.Id, invitee.Id, owner.Id);

        Assert.True(result.Success);

        var updated = await context.UserOrganizations
            .SingleAsync(uo => uo.UserId == invitee.Id && uo.OrganizationId == org.Id);
        Assert.NotNull(updated.LeftAt);
    }

    [Fact]
    public async Task WithdrawInviteAsync_publishes_withdrawn_event()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var invitation = new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id
        };

        context.UserOrganizations.Add(invitation);
        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        await service.WithdrawInviteAsync(org.Id, invitee.Id, owner.Id);

        Assert.True(eventPublisher.OrgMembershipChangedEvents.TryDequeue(out var evt));
        Assert.Equal(MembershipChangeTypes.Withdrawn, evt.ChangeType);
        Assert.Equal(owner.Id, evt.ActorUserId);
    }

    [Fact]
    public async Task WithdrawInviteAsync_fails_for_nonexistent_invitation()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var result = await service.WithdrawInviteAsync(org.Id, Guid.NewGuid(), owner.Id);

        Assert.False(result.Success);
        Assert.Equal("invite_not_found", result.Error);
    }

    [Fact]
    public async Task GetPendingInvitationsForUserAsync_returns_only_pending_invitations()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org1 = await SeedOrganizationAsync(context, owner);
        var org2 = await SeedOrganizationAsync(context, owner, "second-org");

        // Pending invitation
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org1.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        // Active membership (not pending)
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org2.Id,
            Role = "member",
            IsActive = true,
            InvitedByUserId = owner.Id
        });

        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var invitations = await service.GetPendingInvitationsForUserAsync(invitee.Id);

        Assert.Single(invitations);
        Assert.Equal(org1.Id, invitations.First().OrganizationId);
    }

    [Fact]
    public async Task GetPendingInvitationsForUserAsync_excludes_expired_invitations()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee = await SeedUserAsync(context, "invitee@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = invitee.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(-1) // Expired
        });

        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var invitations = await service.GetPendingInvitationsForUserAsync(invitee.Id);

        Assert.Empty(invitations);
    }

    [Fact]
    public async Task MarkExpiredInvitationsAsync_marks_expired_invitations()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var invitee1 = await SeedUserAsync(context, "invitee1@example.com");
        var invitee2 = await SeedUserAsync(context, "invitee2@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        // Expired invitation
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = invitee1.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(-1)
        });

        // Valid invitation
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = invitee2.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,
            InvitedByUserId = owner.Id,
            InvitationExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await context.SaveChangesAsync();

        var eventPublisher = new TestIdentityEventPublisher();
        var service = new MembershipService(
            context,
            TimeProvider.System,
            eventPublisher,
            NullLogger<MembershipService>.Instance);

        var expiredCount = await service.MarkExpiredInvitationsAsync();

        Assert.Equal(1, expiredCount);

        // Use AsNoTracking to get fresh data from database after ExecuteUpdateAsync
        var expired = await context.UserOrganizations
            .AsNoTracking()
            .SingleAsync(uo => uo.UserId == invitee1.Id);
        Assert.NotNull(expired.LeftAt);

        var valid = await context.UserOrganizations
            .AsNoTracking()
            .SingleAsync(uo => uo.UserId == invitee2.Id);
        Assert.Null(valid.LeftAt);
    }

    private IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
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
        User owner,
        string? slug = null)
    {
        var org = new Organization
        {
            Name = "Test Org",
            Slug = slug ?? $"org-{owner.Id:N}",
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
