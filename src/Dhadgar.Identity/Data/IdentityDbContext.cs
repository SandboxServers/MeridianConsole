using Dhadgar.Identity.Data.Configuration;
using Dhadgar.Identity.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore;

namespace Dhadgar.Identity.Data;

public sealed class IdentityDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<UserOrganization> UserOrganizations => Set<UserOrganization>();
    public DbSet<UserOrganizationClaim> UserOrganizationClaims => Set<UserOrganizationClaim>();
    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ClaimDefinition> ClaimDefinitions => Set<ClaimDefinition>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new UserConfiguration());
        builder.ApplyConfiguration(new OrganizationConfiguration());
        builder.ApplyConfiguration(new UserOrganizationConfiguration());
        builder.ApplyConfiguration(new UserOrganizationClaimConfiguration());
        builder.ApplyConfiguration(new LinkedAccountConfiguration());
        builder.ApplyConfiguration(new RefreshTokenConfiguration());
        builder.ApplyConfiguration(new ClaimDefinitionConfiguration());

        builder.UseOpenIddict();

        if (Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        {
            builder.Entity<LinkedAccount>().OwnsOne(la => la.ProviderMetadata, metadata =>
            {
                metadata.Ignore(m => m.ExtraData);
            });

            builder.Entity<Organization>().OwnsOne(o => o.Settings, settings =>
            {
                settings.Ignore(s => s.CustomSettings);
            });
        }

        SeedClaimDefinitions(builder);
    }

    private static void SeedClaimDefinitions(ModelBuilder builder)
    {
        var seedCreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var claims = new List<ClaimDefinition>
        {
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0001-000000000001"),
                Name = "org:read",
                Category = "organization",
                Description = "View organization details",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0001-000000000002"),
                Name = "org:write",
                Category = "organization",
                Description = "Update organization settings",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0001-000000000003"),
                Name = "org:delete",
                Category = "organization",
                Description = "Delete organization",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0001-000000000004"),
                Name = "org:billing",
                Category = "organization",
                Description = "Manage billing and subscriptions",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0002-000000000001"),
                Name = "members:read",
                Category = "members",
                Description = "View organization members",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0002-000000000002"),
                Name = "members:invite",
                Category = "members",
                Description = "Invite new members",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0002-000000000003"),
                Name = "members:remove",
                Category = "members",
                Description = "Remove members",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0002-000000000004"),
                Name = "members:roles",
                Category = "members",
                Description = "Assign member roles",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000001"),
                Name = "servers:read",
                Category = "servers",
                Description = "View servers",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000002"),
                Name = "servers:write",
                Category = "servers",
                Description = "Create and update servers",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000003"),
                Name = "servers:delete",
                Category = "servers",
                Description = "Delete servers",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000004"),
                Name = "servers:start",
                Category = "servers",
                Description = "Start servers",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000005"),
                Name = "servers:stop",
                Category = "servers",
                Description = "Stop servers",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000006"),
                Name = "servers:restart",
                Category = "servers",
                Description = "Restart servers",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0004-000000000001"),
                Name = "nodes:read",
                Category = "nodes",
                Description = "View nodes",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0004-000000000002"),
                Name = "nodes:manage",
                Category = "nodes",
                Description = "Manage node configuration",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0005-000000000001"),
                Name = "files:read",
                Category = "files",
                Description = "View and download files",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0005-000000000002"),
                Name = "files:write",
                Category = "files",
                Description = "Upload and modify files",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0005-000000000003"),
                Name = "files:delete",
                Category = "files",
                Description = "Delete files",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0006-000000000001"),
                Name = "mods:read",
                Category = "mods",
                Description = "View mods",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0006-000000000002"),
                Name = "mods:write",
                Category = "mods",
                Description = "Install and update mods",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            },
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0006-000000000003"),
                Name = "mods:delete",
                Category = "mods",
                Description = "Uninstall mods",
                IsSystemClaim = true,
                CreatedAt = seedCreatedAt
            }
        };

        builder.Entity<ClaimDefinition>().HasData(claims);
    }
}
