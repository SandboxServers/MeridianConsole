using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests;

/// <summary>
/// Tests for RefreshTokenService permission reload functionality.
/// </summary>
public sealed class RefreshTokenServiceTests
{
    [Fact]
    public async Task ReloadUserForRefresh_ReturnsCurrentPermissions()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = TimeProvider.System;
        var permissionService = new PermissionService(context, timeProvider);
        var service = new RefreshTokenService(
            context,
            permissionService,
            timeProvider,
            NullLogger<RefreshTokenService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            ExternalAuthId = Guid.NewGuid().ToString(),
            EmailVerified = true
        };
        context.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org",
            Settings = new OrganizationSettings()
        };
        context.Organizations.Add(org);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = true
        });

        user.PreferredOrganizationId = org.Id;
        await context.SaveChangesAsync();

        // Act
        var result = await service.ReloadUserForRefreshAsync(user.Id, org.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal("admin", result.Role);
        Assert.True(result.EmailVerified);
        // Admin role should have permissions from RoleDefinitions
        Assert.NotEmpty(result.Permissions);
    }

    [Fact]
    public async Task ReloadUserForRefresh_WithDeletedUser_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = TimeProvider.System;
        var permissionService = new PermissionService(context, timeProvider);
        var service = new RefreshTokenService(
            context,
            permissionService,
            timeProvider,
            NullLogger<RefreshTokenService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "deleted@example.com",
            ExternalAuthId = Guid.NewGuid().ToString(),
            DeletedAt = DateTime.UtcNow  // User is deleted
        };
        context.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-2",
            Settings = new OrganizationSettings()
        };
        context.Organizations.Add(org);

        user.PreferredOrganizationId = org.Id;
        await context.SaveChangesAsync();

        // Act
        var result = await service.ReloadUserForRefreshAsync(user.Id, org.Id);

        // Assert: Should fail because user is deleted
        Assert.Null(result);
    }

    [Fact]
    public async Task ReloadUserForRefresh_WithNonexistentUser_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = TimeProvider.System;
        var permissionService = new PermissionService(context, timeProvider);
        var service = new RefreshTokenService(
            context,
            permissionService,
            timeProvider,
            NullLogger<RefreshTokenService>.Instance);

        // Act
        var result = await service.ReloadUserForRefreshAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert: Should fail because user doesn't exist
        Assert.Null(result);
    }

    [Fact]
    public async Task ReloadUserForRefresh_WithInactiveMembership_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = TimeProvider.System;
        var permissionService = new PermissionService(context, timeProvider);
        var service = new RefreshTokenService(
            context,
            permissionService,
            timeProvider,
            NullLogger<RefreshTokenService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "inactive@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-3",
            Settings = new OrganizationSettings()
        };
        context.Organizations.Add(org);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = false,  // Membership is inactive
            LeftAt = DateTime.UtcNow
        });

        user.PreferredOrganizationId = org.Id;
        await context.SaveChangesAsync();

        // Act
        var result = await service.ReloadUserForRefreshAsync(user.Id, org.Id);

        // Assert: Should fail because membership is inactive
        Assert.Null(result);
    }

    [Fact]
    public async Task ReloadUserForRefresh_UsesProvidedOrgId_NotPreferred()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = TimeProvider.System;
        var permissionService = new PermissionService(context, timeProvider);
        var service = new RefreshTokenService(
            context,
            permissionService,
            timeProvider,
            NullLogger<RefreshTokenService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "multiorg@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);

        var org1 = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Org 1",
            Slug = "org-1",
            Settings = new OrganizationSettings()
        };
        context.Organizations.Add(org1);

        var org2 = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Org 2",
            Slug = "org-2",
            Settings = new OrganizationSettings()
        };
        context.Organizations.Add(org2);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org1.Id,
            Role = "owner",
            IsActive = true
        });

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org2.Id,
            Role = "viewer",
            IsActive = true
        });

        // Preferred org is org1
        user.PreferredOrganizationId = org1.Id;
        await context.SaveChangesAsync();

        // Act: Request refresh for org2 specifically
        var result = await service.ReloadUserForRefreshAsync(user.Id, org2.Id);

        // Assert: Should return org2 membership, not org1
        Assert.NotNull(result);
        Assert.Equal(org2.Id, result.Membership?.OrganizationId);
        Assert.Equal("viewer", result.Role);
    }

    [Fact]
    public async Task ReloadUserForRefresh_RecalculatesPermissionsFromDatabase()
    {
        // Arrange: This test verifies that permissions are reloaded, not cached
        using var context = CreateContext();
        var timeProvider = TimeProvider.System;
        var permissionService = new PermissionService(context, timeProvider);
        var service = new RefreshTokenService(
            context,
            permissionService,
            timeProvider,
            NullLogger<RefreshTokenService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "recalc@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-recalc",
            Settings = new OrganizationSettings()
        };
        context.Organizations.Add(org);

        var membership = new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            Role = "viewer",  // Start as viewer
            IsActive = true
        };
        context.UserOrganizations.Add(membership);

        user.PreferredOrganizationId = org.Id;
        await context.SaveChangesAsync();

        // First refresh - should have viewer permissions
        var result1 = await service.ReloadUserForRefreshAsync(user.Id, org.Id);
        Assert.NotNull(result1);
        Assert.Equal("viewer", result1.Role);
        var viewerPermCount = result1.Permissions.Count;

        // Now upgrade user to admin
        membership.Role = "admin";
        await context.SaveChangesAsync();

        // Second refresh - should have admin permissions (more than viewer)
        var result2 = await service.ReloadUserForRefreshAsync(user.Id, org.Id);
        Assert.NotNull(result2);
        Assert.Equal("admin", result2.Role);
        Assert.True(result2.Permissions.Count > viewerPermCount,
            "Admin should have more permissions than viewer after role change");
    }

    [Fact]
    public async Task BuildClaimsIdentity_IncludesAllRequiredClaims()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = TimeProvider.System;
        var permissionService = new PermissionService(context, timeProvider);
        var service = new RefreshTokenService(
            context,
            permissionService,
            timeProvider,
            NullLogger<RefreshTokenService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "claims@example.com",
            DisplayName = "Test User",
            ExternalAuthId = Guid.NewGuid().ToString(),
            EmailVerified = true
        };
        context.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-claims",
            Settings = new OrganizationSettings()
        };
        context.Organizations.Add(org);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            Role = "owner",
            IsActive = true
        });

        user.PreferredOrganizationId = org.Id;
        await context.SaveChangesAsync();

        // Act
        var result = await service.ReloadUserForRefreshAsync(user.Id, org.Id);
        Assert.NotNull(result);

        var identity = result.BuildClaimsIdentity("TestScheme");

        // Assert: Check all required claims are present
        Assert.NotNull(identity.FindFirst("sub"));
        Assert.Equal(user.Id.ToString(), identity.FindFirst("sub")?.Value);

        Assert.NotNull(identity.FindFirst("email"));
        Assert.Equal(user.Email, identity.FindFirst("email")?.Value);

        Assert.NotNull(identity.FindFirst("name"));
        Assert.Equal(user.DisplayName, identity.FindFirst("name")?.Value);

        Assert.NotNull(identity.FindFirst("org_id"));
        Assert.Equal(org.Id.ToString(), identity.FindFirst("org_id")?.Value);

        Assert.NotNull(identity.FindFirst("role"));
        Assert.Equal("owner", identity.FindFirst("role")?.Value);

        Assert.NotNull(identity.FindFirst("email_verified"));
        Assert.Equal("true", identity.FindFirst("email_verified")?.Value);

        // Should have permission claims
        var permissionClaims = identity.FindAll("permission").ToList();
        Assert.NotEmpty(permissionClaims);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"RefreshTokenServiceTests-{Guid.NewGuid()}")
            .Options;

        return new IdentityDbContext(options);
    }
}
