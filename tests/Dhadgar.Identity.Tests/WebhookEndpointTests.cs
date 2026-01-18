using System.Net;
using System.Net.Http.Json;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests;

public sealed class WebhookEndpointTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;

    public WebhookEndpointTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UserDeleted_soft_deletes_user_and_memberships()
    {
        _factory.EventPublisher.Reset();
        var (userId, externalAuthId) = await SeedUserWithOrgAsync();

        var client = _factory.CreateClient();
        var payload = new { @event = "user.deleted", data = new { id = externalAuthId } };
        var response = await client.PostAsJsonAsync("/webhooks/better-auth", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = await db.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.Id == userId);
        var membership = await db.UserOrganizations
            .SingleAsync(uo => uo.UserId == userId);

        Assert.NotNull(user.DeletedAt);
        Assert.False(membership.IsActive);
        Assert.NotNull(membership.LeftAt);

        Assert.True(_factory.EventPublisher.UserDeactivatedEvents.TryDequeue(out var deactivated));
        Assert.Equal(userId, deactivated.UserId);
        Assert.Equal(externalAuthId, deactivated.ExternalAuthId);
    }

    [Fact]
    public async Task PasskeyRegistered_updates_user_flags()
    {
        _factory.EventPublisher.Reset();
        var (userId, externalAuthId) = await SeedUserWithOrgAsync();

        var client = _factory.CreateClient();
        var payload = new { @event = "passkey.registered", data = new { userId = externalAuthId } };
        var response = await client.PostAsJsonAsync("/webhooks/better-auth", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);

        Assert.True(user.HasPasskeysRegistered);
        Assert.NotNull(user.LastPasskeyAuthAt);
    }

    [Fact]
    public async Task UserUpdated_syncs_email()
    {
        _factory.EventPublisher.Reset();
        var (userId, externalAuthId) = await SeedUserWithOrgAsync();

        var client = _factory.CreateClient();
        var payload = new { @event = "user.updated", data = new { id = externalAuthId, email = "new@example.com", emailVerified = true } };
        var response = await client.PostAsJsonAsync("/webhooks/better-auth", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);

        Assert.Equal("new@example.com", user.Email);
        Assert.True(user.EmailVerified);
    }

    private async Task<(Guid UserId, string ExternalAuthId)> SeedUserWithOrgAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var externalAuthId = $"external-{Guid.NewGuid():N}";
        var user = new User
        {
            Id = userId,
            ExternalAuthId = externalAuthId,
            Email = "user@example.com",
            EmailVerified = true
        };

        var org = new Organization
        {
            Name = "Test Org",
            Slug = $"org-{userId:N}",
            OwnerId = userId
        };

        var membership = new UserOrganization
        {
            UserId = userId,
            Organization = org,
            Role = "owner",
            IsActive = true
        };

        db.Users.Add(user);
        db.Organizations.Add(org);
        db.UserOrganizations.Add(membership);
        await db.SaveChangesAsync();

        return (userId, externalAuthId);
    }
}
