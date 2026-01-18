using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class OrganizationOwnershipTests
{
    [Fact]
    public async Task TransferOwnershipAsync_transfers_to_active_member()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var newOwner = await SeedUserAsync(context, "newowner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        // Add new owner as active member
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = newOwner.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(org.Id, owner.Id, newOwner.Id);

        Assert.True(result.Success);

        var updatedOrg = await context.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Equal(newOwner.Id, updatedOrg.OwnerId);
    }

    [Fact]
    public async Task TransferOwnershipAsync_promotes_new_owner_to_owner_role()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var newOwner = await SeedUserAsync(context, "newowner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = newOwner.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        await service.TransferOwnershipAsync(org.Id, owner.Id, newOwner.Id);

        var newOwnerMembership = await context.UserOrganizations
            .SingleAsync(uo => uo.UserId == newOwner.Id && uo.OrganizationId == org.Id);
        Assert.Equal("owner", newOwnerMembership.Role);
    }

    [Fact]
    public async Task TransferOwnershipAsync_demotes_previous_owner_to_admin()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var newOwner = await SeedUserAsync(context, "newowner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = newOwner.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        await service.TransferOwnershipAsync(org.Id, owner.Id, newOwner.Id);

        var previousOwnerMembership = await context.UserOrganizations
            .SingleAsync(uo => uo.UserId == owner.Id && uo.OrganizationId == org.Id);
        Assert.Equal("admin", previousOwnerMembership.Role);
    }

    [Fact]
    public async Task TransferOwnershipAsync_fails_when_transferring_to_self()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(org.Id, owner.Id, owner.Id);

        Assert.False(result.Success);
        Assert.Equal("cannot_transfer_to_self", result.Error);
    }

    [Fact]
    public async Task TransferOwnershipAsync_fails_when_not_owner()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var admin = await SeedUserAsync(context, "admin@example.com");
        var newOwner = await SeedUserAsync(context, "newowner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = admin.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = true
        });
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = newOwner.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(org.Id, admin.Id, newOwner.Id);

        Assert.False(result.Success);
        Assert.Equal("not_owner", result.Error);
    }

    [Fact]
    public async Task TransferOwnershipAsync_fails_for_nonexistent_organization()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var newOwner = await SeedUserAsync(context, "newowner@example.com");

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(Guid.NewGuid(), owner.Id, newOwner.Id);

        Assert.False(result.Success);
        Assert.Equal("org_not_found", result.Error);
    }

    [Fact]
    public async Task TransferOwnershipAsync_fails_for_deleted_organization()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var newOwner = await SeedUserAsync(context, "newowner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        org.DeletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(org.Id, owner.Id, newOwner.Id);

        Assert.False(result.Success);
        Assert.Equal("org_not_found", result.Error);
    }

    [Fact]
    public async Task TransferOwnershipAsync_fails_when_new_owner_not_member()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var nonMember = await SeedUserAsync(context, "nonmember@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(org.Id, owner.Id, nonMember.Id);

        Assert.False(result.Success);
        Assert.Equal("new_owner_not_member", result.Error);
    }

    [Fact]
    public async Task TransferOwnershipAsync_fails_when_new_owner_is_inactive()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var inactiveMember = await SeedUserAsync(context, "inactive@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = inactiveMember.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = false // Inactive
        });
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(org.Id, owner.Id, inactiveMember.Id);

        Assert.False(result.Success);
        Assert.Equal("new_owner_not_member", result.Error);
    }

    [Fact]
    public async Task TransferOwnershipAsync_fails_when_new_owner_has_left()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var leftMember = await SeedUserAsync(context, "left@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = leftMember.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = true,
            LeftAt = DateTime.UtcNow // Has left
        });
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.TransferOwnershipAsync(org.Id, owner.Id, leftMember.Id);

        Assert.False(result.Success);
        Assert.Equal("new_owner_not_member", result.Error);
    }

    [Fact]
    public async Task TransferOwnershipAsync_updates_organization_timestamp()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var newOwner = await SeedUserAsync(context, "newowner@example.com");
        var org = await SeedOrganizationAsync(context, owner);

        var originalUpdatedAt = org.UpdatedAt;

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = newOwner.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new OrganizationService(context, TimeProvider.System);
        await service.TransferOwnershipAsync(org.Id, owner.Id, newOwner.Id);

        var updatedOrg = await context.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.NotNull(updatedOrg.UpdatedAt);
        Assert.NotEqual(originalUpdatedAt, updatedOrg.UpdatedAt);
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
