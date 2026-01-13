using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dhadgar.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using OpenIddict.Abstractions;

namespace Dhadgar.Identity.Tests.Integration;

/// <summary>
/// Integration tests for OpenIddict Client Credentials flow (service-to-service authentication)
/// </summary>
public sealed class ClientCredentialsFlowIntegrationTests : IClassFixture<IdentityWebApplicationFactory>, IAsyncLifetime
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string DevClientId = "dev-client";
    private const string DevClientSecret = "dev-secret";

    public ClientCredentialsFlowIntegrationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Ensure database is created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync(DevClientId) is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = DevClientId,
                ClientSecret = DevClientSecret,
                DisplayName = "Dev Client (Tests)",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "servers:read",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "servers:write",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "nodes:manage",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "billing:read",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "wif"
                }
            };

            await manager.CreateAsync(descriptor);
        }
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ClientCredentials_WithValidCredentials_IssuesAccessToken()
    {
        // Arrange: Client credentials grant request
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = DevClientId,
            ["client_secret"] = DevClientSecret,
            ["scope"] = "servers:read servers:write"
        });

        // Act: Request token
        var response = await _client.PostAsync("/connect/token", request);

        // Assert: Token issued
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("access_token", out var accessTokenProp));
        Assert.True(result.TryGetProperty("token_type", out var tokenTypeProp));
        Assert.True(result.TryGetProperty("expires_in", out var expiresInProp));

        var accessToken = accessTokenProp.GetString();
        Assert.NotNull(accessToken);
        Assert.Equal("Bearer", tokenTypeProp.GetString());
        Assert.True(expiresInProp.GetInt32() > 0);

        // Verify JWT claims
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        Assert.Equal(DevClientId, jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value);
        Assert.Equal(DevClientId, jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value);

        var scopeClaims = jwt.Claims
            .Where(c => c.Type == "scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        Assert.Contains("servers:read", scopeClaims);
        Assert.Contains("servers:write", scopeClaims);
    }

    [Fact]
    public async Task ClientCredentials_WithInvalidClientId_Returns400()
    {
        // Arrange: Invalid client ID
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "invalid-client",
            ["client_secret"] = DevClientSecret,
            ["scope"] = "servers:read"
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert: Invalid client error
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized });

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("error", out var errorProp));
        Assert.Equal("invalid_client", errorProp.GetString());
    }

    [Fact]
    public async Task ClientCredentials_WithInvalidClientSecret_Returns400()
    {
        // Arrange: Invalid client secret
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = DevClientId,
            ["client_secret"] = "wrong-secret",
            ["scope"] = "servers:read"
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert: Invalid client error
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized });

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("error", out var errorProp));
        Assert.Equal("invalid_client", errorProp.GetString());
    }

    [Fact]
    public async Task ClientCredentials_WithUnauthorizedScope_Returns400()
    {
        // Arrange: Request scope not granted to this client
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = DevClientId,
            ["client_secret"] = DevClientSecret,
            ["scope"] = "unauthorized:scope" // Scope not in dev client permissions
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert: Invalid scope error
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("error", out var errorProp));
        Assert.Equal("invalid_scope", errorProp.GetString());
    }

    [Fact]
    public async Task ClientCredentials_WithWifScope_IssuesAzureCompatibleToken()
    {
        // Arrange: Request WIF scope for Azure Workload Identity Federation
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = DevClientId,
            ["client_secret"] = DevClientSecret,
            ["scope"] = "wif"
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert: WIF token issued
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = result.GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        // Verify JWT has Azure-compatible audience
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        var audClaim = jwt.Claims.FirstOrDefault(c => c.Type == "aud")?.Value;
        Assert.Equal("api://AzureADTokenExchange", audClaim);

        // Verify typ header is "JWT" (not "at+jwt") for Azure compatibility
        Assert.Equal("JWT", jwt.Header.Typ);
    }

    [Fact]
    public async Task ClientCredentials_CanRequestMultipleScopes()
    {
        // Arrange: Request multiple scopes
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = DevClientId,
            ["client_secret"] = DevClientSecret,
            ["scope"] = "servers:read servers:write nodes:manage billing:read"
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert: Token with all requested scopes
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = result.GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        var scopeClaims = jwt.Claims
            .Where(c => c.Type == "scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        Assert.Contains("servers:read", scopeClaims);
        Assert.Contains("servers:write", scopeClaims);
        Assert.Contains("nodes:manage", scopeClaims);
        Assert.Contains("billing:read", scopeClaims);
    }

    [Fact]
    public async Task ClientCredentials_WithMissingGrantType_Returns400()
    {
        // Arrange: Missing grant_type
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = DevClientId,
            ["client_secret"] = DevClientSecret,
            ["scope"] = "servers:read"
        });

        // Act
        var response = await _client.PostAsync("/connect/token", request);

        // Assert: Invalid request error
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("error", out var errorProp));
        Assert.Equal("invalid_request", errorProp.GetString());
    }

    [Fact]
    public async Task WellKnownJwks_ReturnsPublicSigningKey()
    {
        // Act: Fetch JWKS endpoint
        var response = await _client.GetAsync("/.well-known/jwks.json");

        // Assert: Public key exposed
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jwks = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(jwks.TryGetProperty("keys", out var keysProp));
        Assert.True(keysProp.GetArrayLength() > 0);

        // Verify key has required fields
        var firstKey = keysProp[0];
        Assert.True(firstKey.TryGetProperty("kty", out _)); // Key type
        Assert.True(firstKey.TryGetProperty("use", out var useProp)); // Key use
        Assert.Equal("sig", useProp.GetString()); // Signing key

        // Verify it's a public key (no private key material)
        Assert.False(firstKey.TryGetProperty("d", out _)); // RSA private exponent should not be present
    }

    [Fact]
    public async Task WellKnownOpenIdConfiguration_ReturnsDiscoveryDocument()
    {
        // Act: Fetch OpenID Connect discovery document
        var response = await _client.GetAsync("/.well-known/openid-configuration");

        // Assert: Discovery document returned
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var config = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Verify required endpoints
        Assert.True(config.TryGetProperty("issuer", out var issuerProp));
        Assert.True(config.TryGetProperty("authorization_endpoint", out _));
        Assert.True(config.TryGetProperty("token_endpoint", out _));
        Assert.True(config.TryGetProperty("userinfo_endpoint", out _));
        Assert.True(config.TryGetProperty("jwks_uri", out _));

        // Verify supported grants
        Assert.True(config.TryGetProperty("grant_types_supported", out var grantsProp));
        var grants = grantsProp.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("client_credentials", grants);
        Assert.Contains("authorization_code", grants);
        Assert.Contains("refresh_token", grants);
    }
}
