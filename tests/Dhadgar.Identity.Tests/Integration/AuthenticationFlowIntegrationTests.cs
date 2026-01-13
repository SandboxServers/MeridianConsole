using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Identity.Tests.Integration;

/// <summary>
/// Integration tests for end-to-end authentication flows:
/// Better Auth → Token Exchange → JWT → Gateway header injection
/// </summary>
public sealed class AuthenticationFlowIntegrationTests : IClassFixture<IdentityWebApplicationFactory>, IAsyncLifetime
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private ExchangeTokenOptions? _exchangeOptions;

    public AuthenticationFlowIntegrationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        _exchangeOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<ExchangeTokenOptions>>()
            .Value;

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TokenExchange_WithValidExchangeToken_CreatesUserAndIssuesJWT()
    {
        // Arrange: Create exchange token (simulating Better Auth)
        var userId = Guid.NewGuid().ToString("N");
        var email = "testuser@example.com";
        var exchangeToken = CreateExchangeToken(userId, email, "discord", "discord_123456");

        var request = new
        {
            exchangeToken
        };

        // Act: Exchange token for JWT
        var response = await _client.PostAsJsonAsync("/exchange", request);

        // Assert: Successful exchange
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            Assert.Fail(errorBody);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("accessToken", out var accessTokenProp));
        Assert.True(result.TryGetProperty("refreshToken", out var refreshTokenProp));
        Assert.True(result.TryGetProperty("expiresIn", out var expiresInProp));

        var accessToken = accessTokenProp.GetString();
        var refreshToken = refreshTokenProp.GetString();

        Assert.NotNull(accessToken);
        Assert.NotNull(refreshToken);
        Assert.True(expiresInProp.GetInt32() > 0);

        // Verify JWT claims
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        Assert.Equal(email, jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value);
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value);
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value); // Default org created

        // Verify user was created in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.False(user.EmailVerified);

        // Verify Better Auth login was created
        var login = await db.UserLogins.FirstOrDefaultAsync(l => l.UserId == user.Id);
        Assert.NotNull(login);
        Assert.Equal("betterauth", login.LoginProvider);
        Assert.Equal(userId, login.ProviderKey);

        // Verify organization membership
        var membership = await db.UserOrganizations
            .Include(uo => uo.Organization)
            .FirstOrDefaultAsync(uo => uo.UserId == user.Id);
        Assert.NotNull(membership);
        Assert.NotNull(membership.Organization);
        Assert.Equal("owner", membership.Role);
    }

    [Fact]
    public async Task TokenExchange_WithReplayedToken_Returns401()
    {
        // Arrange: Create exchange token
        var userId = Guid.NewGuid().ToString("N");
        var exchangeToken = CreateExchangeToken(userId, "replay@example.com", "discord", "discord_replay");

        var request = new { exchangeToken };

        // Act: First exchange (should succeed)
        var firstResponse = await _client.PostAsJsonAsync("/exchange", request);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act: Second exchange with same token (should fail - replay attack)
        var replayResponse = await _client.PostAsJsonAsync("/exchange", request);

        // Assert: Replay rejected
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
    }

    [Fact]
    public async Task TokenExchange_WithExpiredToken_Returns401()
    {
        // Arrange: Create expired exchange token (issued 5 minutes ago, 60s expiry)
        var userId = Guid.NewGuid().ToString("N");
        var expiredToken = CreateExchangeToken(
            userId,
            "expired@example.com",
            "discord",
            "discord_expired",
            issuedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            expiresIn: TimeSpan.FromSeconds(60));

        var request = new { exchangeToken = expiredToken };

        // Act
        var response = await _client.PostAsJsonAsync("/exchange", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenExchange_WithInvalidSignature_Returns401()
    {
        // Arrange: Create token with wrong signing key
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var userId = Guid.NewGuid().ToString("N");

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _exchangeOptions?.Issuer,
            Audience = _exchangeOptions?.Audience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", userId),
                new Claim("email", "invalid@example.com"),
                new Claim("provider", "discord"),
                new Claim("provider_user_id", "discord_invalid"),
                new Claim("purpose", "token_exchange"),
                new Claim("jti", Guid.NewGuid().ToString("N"))
            }),
            Expires = DateTime.UtcNow.AddSeconds(60),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(wrongKey),
                SecurityAlgorithms.EcdsaSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();
        var invalidToken = handler.CreateEncodedJwt(tokenDescriptor);

        var request = new { exchangeToken = invalidToken };

        // Act
        var response = await _client.PostAsJsonAsync("/exchange", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenExchange_CreatesDefaultOrganization_WithOwnerRole()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString("N");
        var email = "orgtest@example.com";
        var exchangeToken = CreateExchangeToken(userId, email, "discord", "discord_orgtest");

        var request = new { exchangeToken };

        // Act
        var response = await _client.PostAsJsonAsync("/exchange", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify organization was created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        Assert.NotNull(user);

        var membership = await db.UserOrganizations
            .Include(uo => uo.Organization)
            .FirstOrDefaultAsync(uo => uo.UserId == user.Id);

        Assert.NotNull(membership);
        Assert.NotNull(membership.Organization);
        Assert.Equal("owner", membership.Role);
        Assert.Equal("Default Organization", membership.Organization.Name);
        Assert.NotNull(membership.Organization.Slug);
    }

    [Fact]
    public async Task TokenExchange_PublishesUserAuthenticatedEvent()
    {
        // Arrange
        _factory.EventPublisher.Reset();

        var userId = Guid.NewGuid().ToString("N");
        var email = "eventtest@example.com";
        var exchangeToken = CreateExchangeToken(userId, email, "discord", "discord_eventtest");

        var request = new { exchangeToken };

        // Act
        await _client.PostAsJsonAsync("/exchange", request);

        // Assert: Event was published
        var events = _factory.EventPublisher.UserAuthenticatedEvents.ToArray();
        Assert.Single(events);
        Assert.Equal(email, events[0].Email);
        Assert.Equal(userId, events[0].ExternalAuthId);
    }

    /// <summary>
    /// Creates an ES256-signed exchange token simulating Better Auth
    /// </summary>
    private string CreateExchangeToken(
        string userId,
        string email,
        string provider,
        string providerUserId,
        DateTimeOffset? issuedAt = null,
        TimeSpan? expiresIn = null)
    {
        if (_exchangeOptions is null)
        {
            throw new InvalidOperationException("Exchange token options not initialized.");
        }

        var signingKey = IdentityWebApplicationFactory.CreateExchangeTokenKey();

        var iat = issuedAt ?? DateTimeOffset.UtcNow;
        var exp = expiresIn ?? TimeSpan.FromSeconds(60);

        var securityKey = new ECDsaSecurityKey(signingKey)
        {
            CryptoProviderFactory = new CryptoProviderFactory
            {
                CacheSignatureProviders = false
            }
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _exchangeOptions.Issuer,
            Audience = _exchangeOptions.Audience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", userId),
                new Claim("email", email),
                new Claim("provider", provider),
                new Claim("provider_user_id", providerUserId),
                new Claim("purpose", "token_exchange"),
                new Claim("jti", Guid.NewGuid().ToString("N"))
            }),
            Expires = iat.Add(exp).UtcDateTime,
            IssuedAt = iat.UtcDateTime,
            NotBefore = iat.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.EcdsaSha256)
        };

        var handler = new JsonWebTokenHandler();
        try
        {
            return handler.CreateToken(tokenDescriptor);
        }
        finally
        {
            signingKey.Dispose();
        }
    }
}
