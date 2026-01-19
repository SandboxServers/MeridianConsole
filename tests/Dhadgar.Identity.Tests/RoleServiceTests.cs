using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests;

/// <summary>
/// Tests for RoleService, particularly privilege escalation prevention.
/// </summary>
public sealed class RoleServiceTests : IDisposable
{
    private readonly IdentityDbContext _dbContext;
    private bool _disposed;
    private readonly TimeProvider _timeProvider;
    private readonly RoleService _roleService;
    private readonly PermissionService _permissionService;

    public RoleServiceTests()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"RoleServiceTests-{Guid.NewGuid()}")
            .Options;

        _dbContext = new IdentityDbContext(options);
        _timeProvider = TimeProvider.System;
        _permissionService = new PermissionService(_dbContext, _timeProvider);
        _roleService = new RoleService(
            _dbContext,
            _permissionService,
            _timeProvider,
            NullLogger<RoleService>.Instance);
    }

    [Fact]
    public async Task CreateRole_WithUnownedPermissions_RejectsPreventsPrivilegeEscalation()
    {
        // Arrange: Create user with limited permissions
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org",
            Settings = new OrganizationSettings()
        };
        _dbContext.Organizations.Add(org);

        var actor = new User
        {
            Id = Guid.NewGuid(),
            Email = "actor@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        _dbContext.Users.Add(actor);

        // Actor only has servers:read permission
        _dbContext.UserOrganizations.Add(new UserOrganization
        {
            UserId = actor.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });

        // Add claim definitions for the permissions
        _dbContext.ClaimDefinitions.AddRange(
            new ClaimDefinition { Name = "servers:read", Description = "Read servers", Category = "servers" },
            new ClaimDefinition { Name = "servers:write", Description = "Write servers", Category = "servers" },
            new ClaimDefinition { Name = "billing:read", Description = "Read billing", Category = "billing" }
        );

        await _dbContext.SaveChangesAsync();

        // Act: Try to create a role with permissions actor doesn't have
        var request = new RoleCreateRequest(
            "ElevatedRole",
            "A role with elevated permissions",
            new[] { "servers:write", "billing:read" });

        var result = await _roleService.CreateAsync(org.Id, actor.Id, request);

        // Assert: Should fail with privilege escalation error
        Assert.False(result.Success);
        Assert.Equal("cannot_grant_unowned_permissions", result.Error);
    }

    [Fact]
    public async Task CreateRole_WithOwnedPermissions_Succeeds()
    {
        // Arrange: Create user with owner role (has all permissions)
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-2",
            Settings = new OrganizationSettings()
        };
        _dbContext.Organizations.Add(org);

        var actor = new User
        {
            Id = Guid.NewGuid(),
            Email = "owner@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        _dbContext.Users.Add(actor);

        // Actor is owner with all permissions
        _dbContext.UserOrganizations.Add(new UserOrganization
        {
            UserId = actor.Id,
            OrganizationId = org.Id,
            Role = "owner",
            IsActive = true
        });

        // Add claim definitions
        _dbContext.ClaimDefinitions.AddRange(
            new ClaimDefinition { Name = "servers:read", Description = "Read servers", Category = "servers" },
            new ClaimDefinition { Name = "servers:write", Description = "Write servers", Category = "servers" }
        );

        await _dbContext.SaveChangesAsync();

        // Act: Create a role with permissions actor has (owner has all)
        var request = new RoleCreateRequest(
            "ServerManager",
            "Can manage servers",
            new[] { "servers:read", "servers:write" });

        var result = await _roleService.CreateAsync(org.Id, actor.Id, request);

        // Assert: Should succeed
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("ServerManager", result.Value.Name);
        Assert.Contains("servers:read", result.Value.Permissions);
        Assert.Contains("servers:write", result.Value.Permissions);
    }

    [Fact]
    public async Task CreateRole_WithNoPermissions_Succeeds()
    {
        // Arrange
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-3",
            Settings = new OrganizationSettings()
        };
        _dbContext.Organizations.Add(org);

        var actor = new User
        {
            Id = Guid.NewGuid(),
            Email = "viewer@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        _dbContext.Users.Add(actor);

        _dbContext.UserOrganizations.Add(new UserOrganization
        {
            UserId = actor.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });

        await _dbContext.SaveChangesAsync();

        // Act: Create a role with no permissions (allowed for anyone with members:roles)
        var request = new RoleCreateRequest(
            "EmptyRole",
            "A role with no permissions",
            Array.Empty<string>());

        var result = await _roleService.CreateAsync(org.Id, actor.Id, request);

        // Assert: Should succeed (no privilege escalation possible with empty permissions)
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.Permissions);
    }

    [Fact]
    public async Task AssignRole_WithUnownedPermissions_RejectsPreventsPrivilegeEscalation()
    {
        // Arrange: Create actors and a custom role with elevated permissions
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-4",
            Settings = new OrganizationSettings()
        };
        _dbContext.Organizations.Add(org);

        var adminActor = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        _dbContext.Users.Add(adminActor);

        var targetUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "target@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        _dbContext.Users.Add(targetUser);

        // Admin has admin role but NOT billing:admin permission
        _dbContext.UserOrganizations.Add(new UserOrganization
        {
            UserId = adminActor.Id,
            OrganizationId = org.Id,
            Role = "admin",
            IsActive = true
        });

        _dbContext.UserOrganizations.Add(new UserOrganization
        {
            UserId = targetUser.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });

        // Create a custom role with billing:admin permission (which admin doesn't have)
        var elevatedRole = new OrganizationRole
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "BillingAdmin",
            NormalizedName = "BILLINGADMIN",
            Permissions = new System.Collections.ObjectModel.Collection<string>(new[] { "billing:admin" })
        };
        _dbContext.OrganizationRoles.Add(elevatedRole);

        await _dbContext.SaveChangesAsync();

        // Act: Admin tries to assign the elevated role to target user
        var membershipService = new MembershipService(
            _dbContext,
            _timeProvider,
            new TestIdentityEventPublisher(),
            NullLogger<MembershipService>.Instance);

        var result = await _roleService.AssignRoleAsync(
            org.Id,
            adminActor.Id,
            targetUser.Id,
            elevatedRole.Id.ToString(),
            membershipService);

        // Assert: Should fail with privilege escalation error
        Assert.False(result.Success);
        Assert.Equal("cannot_assign_role_with_unowned_permissions", result.Error);
    }

    [Fact]
    public async Task AssignSystemRole_Succeeds()
    {
        // Arrange: Owner assigns admin role to a user
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = "test-org-5",
            Settings = new OrganizationSettings()
        };
        _dbContext.Organizations.Add(org);

        var owner = new User
        {
            Id = Guid.NewGuid(),
            Email = "owner@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        _dbContext.Users.Add(owner);

        var targetUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "target@example.com",
            ExternalAuthId = Guid.NewGuid().ToString()
        };
        _dbContext.Users.Add(targetUser);

        _dbContext.UserOrganizations.Add(new UserOrganization
        {
            UserId = owner.Id,
            OrganizationId = org.Id,
            Role = "owner",
            IsActive = true
        });

        _dbContext.UserOrganizations.Add(new UserOrganization
        {
            UserId = targetUser.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });

        await _dbContext.SaveChangesAsync();

        // Act: Owner assigns system admin role
        var membershipService = new MembershipService(
            _dbContext,
            _timeProvider,
            new TestIdentityEventPublisher(),
            NullLogger<MembershipService>.Instance);

        var result = await _roleService.AssignRoleAsync(
            org.Id,
            owner.Id,
            targetUser.Id,
            "admin",
            membershipService);

        // Assert: Should succeed (system roles don't check permission subset for owners)
        Assert.True(result.Success);
        Assert.Equal("admin", result.Value?.Role);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _dbContext.Dispose();
            _disposed = true;
        }
    }
}
