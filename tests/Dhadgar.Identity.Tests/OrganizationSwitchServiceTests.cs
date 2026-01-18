using System.Security.Claims;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class OrganizationSwitchServiceTests
{
    [Fact]
    public async Task SwitchAsync_updates_preferred_org_and_issues_tokens()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var user = await SeedUserAsync(context);
        var org = await SeedOrganizationAsync(context, user);

        context.UserOrganizations.Add(new UserOrganization
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            Role = "viewer",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var permissionService = new PermissionService(context, TimeProvider.System);
        var jwtService = new TestJwtService();
        var options = new OptionsWrapper<AuthOptions>(new AuthOptions { RefreshTokenLifetimeDays = 7 });
        var service = new OrganizationSwitchService(context, jwtService, permissionService, TimeProvider.System, options);

        var outcome = await service.SwitchAsync(user.Id, org.Id);

        Assert.True(outcome.Success);
        Assert.Equal("access-token", outcome.AccessToken);
        Assert.Equal("refresh-token", outcome.RefreshToken);
        Assert.Equal(org.Id, outcome.OrganizationId);
        Assert.Equal(org.Id, (await context.Users.SingleAsync(u => u.Id == user.Id)).PreferredOrganizationId);
        Assert.Single(context.RefreshTokens);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityDbContext(options);
    }

    private static async Task<User> SeedUserAsync(IdentityDbContext context)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "switch@example.com",
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
            Name = "Switch Org",
            Slug = $"switch-{owner.Id:N}",
            OwnerId = owner.Id
        };

        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org;
    }

    private sealed class TestJwtService : IJwtService
    {
        public Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
            IEnumerable<Claim> claims,
            CancellationToken ct = default)
            => Task.FromResult(("access-token", "refresh-token", 900));
    }
}
