using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class LinkedAccountServiceTests
{
    [Fact]
    public async Task LinkAsync_creates_new_linked_account()
    {
        using var context = CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "link@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new LinkedAccountService(context, TimeProvider.System, NullLogger<LinkedAccountService>.Instance);
        var metadata = new LinkedAccountMetadata { DisplayName = "SteamUser" };

        var result = await service.LinkAsync(
            user.Id,
            new ExternalAccountInfo("steam", "steam-123", metadata));

        Assert.True(result.Success);
        Assert.NotNull(result.Account);
        Assert.Equal("steam", result.Account?.Provider);
        Assert.Equal("steam-123", result.Account?.ProviderAccountId);
    }

    [Fact]
    public async Task LinkAsync_rejects_linked_account_owned_by_other_user()
    {
        using var context = CreateContext();
        var userA = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "a@example.com"
        };
        var userB = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = "b@example.com"
        };

        context.Users.AddRange(userA, userB);
        context.LinkedAccounts.Add(new LinkedAccount
        {
            UserId = userA.Id,
            Provider = "steam",
            ProviderAccountId = "steam-999"
        });
        await context.SaveChangesAsync();

        var service = new LinkedAccountService(context, TimeProvider.System, NullLogger<LinkedAccountService>.Instance);

        var result = await service.LinkAsync(
            userB.Id,
            new ExternalAccountInfo("steam", "steam-999", new LinkedAccountMetadata()));

        Assert.False(result.Success);
        Assert.Equal("account_already_linked", result.Error);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityDbContext(options);
    }
}
