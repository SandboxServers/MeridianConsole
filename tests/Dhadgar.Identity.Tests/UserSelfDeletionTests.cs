using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class UserSelfDeletionTests : IDisposable
{
    // Use SQLite in-memory with a shared connection for the test class
    // This enables ExecuteUpdateAsync/ExecuteDeleteAsync which in-memory provider doesn't support
    private readonly SqliteConnection _connection;

    public UserSelfDeletionTests()
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
    public async Task RequestDeletionAsync_sets_scheduled_deletion_date()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context, "user@example.com");
        var service = CreateUserService(context);

        var result = await service.RequestDeletionAsync(user.Id);

        Assert.True(result.Success);
        Assert.True(result.Value > DateTime.UtcNow.AddDays(29));
        Assert.True(result.Value < DateTime.UtcNow.AddDays(31));
    }

    [Fact]
    public async Task RequestDeletionAsync_revokes_all_refresh_tokens()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context, "user@example.com");
        var org1 = await SeedOrganizationAsync(context, user);
        var org2 = await SeedOrganizationAsync(context, user, "org2");

        // Add some refresh tokens (using real organization IDs for SQLite foreign key compliance)
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            OrganizationId = org1.Id,
            TokenHash = "hash1",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            OrganizationId = org2.Id,
            TokenHash = "hash2",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await context.SaveChangesAsync();

        var service = CreateUserService(context);
        await service.RequestDeletionAsync(user.Id);

        // Use AsNoTracking to get fresh data from database after ExecuteUpdateAsync
        var tokens = await context.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .ToListAsync();

        Assert.All(tokens, t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task RequestDeletionAsync_deactivates_all_memberships()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context, "user@example.com");
        var org1 = await SeedOrganizationAsync(context, user);
        var org2 = await SeedOrganizationAsync(context, user, "org2");

        var service = CreateUserService(context);
        await service.RequestDeletionAsync(user.Id);

        // Use AsNoTracking to get fresh data from database after ExecuteUpdateAsync
        var memberships = await context.UserOrganizations
            .AsNoTracking()
            .Where(uo => uo.UserId == user.Id)
            .ToListAsync();

        Assert.All(memberships, m =>
        {
            Assert.False(m.IsActive);
            Assert.NotNull(m.LeftAt);
        });
    }

    [Fact]
    public async Task RequestDeletionAsync_fails_for_already_deleted_user()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context, "user@example.com");
        user.DeletedAt = DateTime.UtcNow.AddDays(-1); // Already deleted
        await context.SaveChangesAsync();

        var service = CreateUserService(context);
        var result = await service.RequestDeletionAsync(user.Id);

        Assert.False(result.Success);
        Assert.Equal("user_not_found", result.Error);
    }

    [Fact]
    public async Task CancelDeletionAsync_clears_scheduled_deletion()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context, "user@example.com");
        user.DeletedAt = DateTime.UtcNow.AddDays(30); // Scheduled for deletion
        await context.SaveChangesAsync();

        var service = CreateUserService(context);
        var result = await service.CancelDeletionAsync(user.Id);

        Assert.True(result.Success);

        // Use IgnoreQueryFilters to verify the update (though it should now have DeletedAt = null)
        var updated = await context.Users
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Id == user.Id);
        Assert.Null(updated.DeletedAt);
    }

    [Fact]
    public async Task CancelDeletionAsync_fails_if_deletion_already_processed()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context, "user@example.com");
        user.DeletedAt = DateTime.UtcNow.AddDays(-1); // Already deleted in the past
        await context.SaveChangesAsync();

        var service = CreateUserService(context);
        var result = await service.CancelDeletionAsync(user.Id);

        Assert.False(result.Success);
        Assert.Equal("user_not_found_or_already_deleted", result.Error);
    }

    [Fact]
    public async Task CancelDeletionAsync_fails_for_user_without_pending_deletion()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context, "user@example.com");

        var service = CreateUserService(context);
        var result = await service.CancelDeletionAsync(user.Id);

        Assert.False(result.Success);
        Assert.Equal("user_not_found_or_already_deleted", result.Error);
    }

    [Fact]
    public async Task SoftDeleteAsync_revokes_organization_specific_tokens()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);
        var otherOrg = await SeedOrganizationAsync(context, owner, "other-org");

        // Add member to org
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });

        // Add member to other org
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = otherOrg.Id,
            Role = "viewer",
            IsActive = true
        });

        // Add tokens for both orgs
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            TokenHash = "org-token",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = member.Id,
            OrganizationId = otherOrg.Id,
            TokenHash = "other-org-token",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await context.SaveChangesAsync();

        var service = CreateUserService(context);
        await service.SoftDeleteAsync(org.Id, member.Id);

        // Use AsNoTracking to get fresh data from database after ExecuteUpdateAsync
        var orgToken = await context.RefreshTokens
            .AsNoTracking()
            .SingleAsync(t => t.OrganizationId == org.Id && t.UserId == member.Id);
        var otherOrgToken = await context.RefreshTokens
            .AsNoTracking()
            .SingleAsync(t => t.OrganizationId == otherOrg.Id && t.UserId == member.Id);

        Assert.NotNull(orgToken.RevokedAt); // Revoked
        Assert.Null(otherOrgToken.RevokedAt); // Not revoked
    }

    [Fact]
    public async Task SoftDeleteAsync_revokes_all_tokens_when_last_membership_removed()
    {
        using var context = CreateContext();
        var owner = await SeedUserAsync(context, "owner@example.com");
        var member = await SeedUserAsync(context, "member@example.com");
        var org = await SeedOrganizationAsync(context, owner);
        var anotherOrg = await SeedOrganizationAsync(context, owner, "another-org");

        // Add member to single org (not a member of anotherOrg)
        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = member.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });

        // Add token for another org that member doesn't belong to
        // (orphan token scenario - token exists but no membership)
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = member.Id,
            OrganizationId = anotherOrg.Id,
            TokenHash = "orphan-token",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await context.SaveChangesAsync();

        var service = CreateUserService(context);
        await service.SoftDeleteAsync(org.Id, member.Id);

        // Use AsNoTracking to get fresh data from database after ExecuteUpdateAsync
        var orphanToken = await context.RefreshTokens
            .AsNoTracking()
            .SingleAsync(t => t.OrganizationId == anotherOrg.Id && t.UserId == member.Id);

        Assert.NotNull(orphanToken.RevokedAt);
    }

    private IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new IdentityDbContext(options);
    }

    private static UserService CreateUserService(IdentityDbContext context)
    {
        return new UserService(
            context,
            new UpperInvariantLookupNormalizer(),
            TimeProvider.System);
    }

    private static async Task<User> SeedUserAsync(IdentityDbContext context, string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
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

        // Ensure owner membership exists if not already
        var existingMembership = await context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.UserId == owner.Id && uo.OrganizationId == org.Id);

        if (existingMembership is null)
        {
            context.UserOrganizations.Add(new UserOrganization
            {
                UserId = owner.Id,
                OrganizationId = org.Id,
                Role = "owner",
                IsActive = true
            });
        }

        await context.SaveChangesAsync();
        return org;
    }
}
