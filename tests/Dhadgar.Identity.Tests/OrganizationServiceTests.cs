using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class OrganizationServiceTests
{
    [Fact]
    public async Task CreateAsync_creates_org_and_owner_membership()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context);

        var service = new OrganizationService(context, TimeProvider.System);
        var result = await service.CreateAsync(user.Id, new OrganizationCreateRequest("Acme Corp", null));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("acme-corp", result.Value?.Slug);

        var membership = await context.UserOrganizations.SingleAsync(uo => uo.UserId == user.Id);
        Assert.Equal("owner", membership.Role);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public async Task CreateAsync_generates_unique_slug()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context);
        var service = new OrganizationService(context, TimeProvider.System);

        var first = await service.CreateAsync(user.Id, new OrganizationCreateRequest("Acme", null));
        var second = await service.CreateAsync(user.Id, new OrganizationCreateRequest("Acme", null));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotNull(second.Value);
        Assert.Equal("acme-2", second.Value?.Slug);
    }

    [Fact]
    public async Task UpdateAsync_updates_name_and_settings()
    {
        using var context = CreateContext();
        var user = await SeedUserAsync(context);
        var service = new OrganizationService(context, TimeProvider.System);

        var created = await service.CreateAsync(user.Id, new OrganizationCreateRequest("Initial", null));
        var org = created.Value!;

        var settings = new OrganizationSettings
        {
            AllowMemberInvites = false,
            RequireEmailVerification = false,
            MaxMembers = 25
        };

        var update = await service.UpdateAsync(org.Id, new OrganizationUpdateRequest("Updated", null, settings));

        Assert.True(update.Success);
        Assert.Equal("Updated", update.Value?.Name);
        Assert.False(update.Value?.Settings.AllowMemberInvites);
        Assert.Equal(25, update.Value?.Settings.MaxMembers);
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
        await context.Database.EnsureCreatedAsync();
        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "owner@example.com",
            EmailVerified = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
