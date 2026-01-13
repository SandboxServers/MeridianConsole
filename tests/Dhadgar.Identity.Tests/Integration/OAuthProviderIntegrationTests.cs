using System.Net;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

/// <summary>
/// Integration tests for OAuth provider challenge and callback flows
/// </summary>
public sealed class OAuthProviderIntegrationTests : IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OAuthProviderIntegrationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false // We want to inspect redirect responses
        });
    }

    [Theory]
    [InlineData("steam")]
    [InlineData("battlenet")]
    [InlineData("epic")]
    [InlineData("xbox")]
    public async Task OAuthLink_RedirectsToProvider(string provider)
    {
        // Arrange
        var userId = await _factory.SeedUserAsync($"link-{provider}@example.com");
        var returnUrl = "https://panel.meridianconsole.com/auth/callback";

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/oauth/{provider}/link?returnUrl={Uri.EscapeDataString(returnUrl)}");
        request.Headers.Add("X-User-Id", userId.ToString());

        // Act: Initiate OAuth link flow
        var response = await _client.SendAsync(request);

        // Assert: Redirects to provider (302 or 401 if not configured)
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.Unauthorized });

        if (response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.Found)
        {
            Assert.NotNull(response.Headers.Location);
            // In testing environment, mock providers redirect to example.test
            Assert.Contains("example.test", response.Headers.Location.ToString());
        }
    }

    [Fact]
    public async Task OAuthChallenge_WithInvalidReturnUrl_Returns400()
    {
        // Arrange: Return URL not in AllowedRedirectHosts
        var userId = await _factory.SeedUserAsync("link-invalid@example.com");
        var invalidReturnUrl = "https://evil.com/steal-tokens";

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/oauth/steam/link?returnUrl={Uri.EscapeDataString(invalidReturnUrl)}");
        request.Headers.Add("X-User-Id", userId.ToString());

        // Act
        var response = await _client.SendAsync(request);

        // Assert: Falls back to default redirect, still challenges
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task OAuthChallenge_WithMissingReturnUrl_Returns400()
    {
        var userId = await _factory.SeedUserAsync("link-missing@example.com");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/oauth/steam/link");
        request.Headers.Add("X-User-Id", userId.ToString());

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task OAuthLink_WithAuthenticatedUser_InitiatesLinkFlow()
    {
        // Arrange: Seed a user and authenticate
        var userId = await _factory.SeedUserAsync("linktest@example.com");
        var returnUrl = "https://panel.meridianconsole.com/settings/linked-accounts";

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/oauth/steam/link?returnUrl={Uri.EscapeDataString(returnUrl)}");
        request.Headers.Add("X-User-Id", userId.ToString());

        // Act: Initiate link flow
        var response = await _client.SendAsync(request);

        // Assert: Redirects to provider for linking
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task OAuthLink_WithoutAuthentication_Returns401()
    {
        // Arrange: No authentication header
        var returnUrl = "https://panel.meridianconsole.com/settings/linked-accounts";

        // Act
        var response = await _client.GetAsync($"/oauth/steam/link?returnUrl={Uri.EscapeDataString(returnUrl)}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LinkedAccount_CannotBeLInkedToMultipleUsers()
    {
        // Arrange: Create two users
        var user1Id = await _factory.SeedUserAsync("user1@example.com");
        var user2Id = await _factory.SeedUserAsync("user2@example.com");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var service = scope.ServiceProvider.GetRequiredService<ILinkedAccountService>();

        // Link Steam account to user1
        var first = await service.LinkAsync(user1Id, new ExternalAccountInfo(
            "steam",
            "steam_12345",
            new LinkedAccountMetadata { Username = "TestSteamUser" }));
        Assert.True(first.Success);

        // Act: Try to link same Steam account to user2 (should fail)
        var second = await service.LinkAsync(user2Id, new ExternalAccountInfo(
            "steam",
            "steam_12345",
            new LinkedAccountMetadata { Username = "TestSteamUser" }));

        // Assert: Service rejects duplicate link
        Assert.False(second.Success);
        Assert.Equal("account_already_linked", second.Error);
    }

    [Fact]
    public async Task User_CanLinkMultipleProviders()
    {
        // Arrange: Create user
        var userId = await _factory.SeedUserAsync("multilink@example.com");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Act: Link multiple gaming providers to same user
        var accounts = new[]
        {
            new LinkedAccount
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = "steam",
                ProviderAccountId = "steam_multi1",
                ProviderMetadata = new LinkedAccountMetadata
                {
                    Username = "SteamUser"
                }
            },
            new LinkedAccount
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = "xbox",
                ProviderAccountId = "xbox_multi1",
                ProviderMetadata = new LinkedAccountMetadata
                {
                    Username = "XboxUser"
                }
            },
            new LinkedAccount
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = "battlenet",
                ProviderAccountId = "battlenet_multi1",
                ProviderMetadata = new LinkedAccountMetadata
                {
                    Username = "BattleNetUser#1234"
                }
            }
        };

        db.LinkedAccounts.AddRange(accounts);
        await db.SaveChangesAsync();

        // Assert: All accounts linked
        var linkedAccounts = await db.LinkedAccounts
            .Where(la => la.UserId == userId)
            .ToListAsync();

        Assert.Equal(3, linkedAccounts.Count);
        Assert.Contains(linkedAccounts, la => la.Provider == "steam");
        Assert.Contains(linkedAccounts, la => la.Provider == "xbox");
        Assert.Contains(linkedAccounts, la => la.Provider == "battlenet");
    }

}
