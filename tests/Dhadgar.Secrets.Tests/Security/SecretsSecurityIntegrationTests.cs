using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dhadgar.Secrets.Authorization;
using Dhadgar.Secrets.Audit;
using Dhadgar.Secrets.Endpoints;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Dhadgar.Secrets.Tests.Security;

public sealed class SecretsSecurityIntegrationTests : IClassFixture<SecureSecretsWebApplicationFactory>
{
    private readonly SecureSecretsWebApplicationFactory _factory;

    public SecretsSecurityIntegrationTests(SecureSecretsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Authentication Tests

    [Fact]
    public async Task GetSecret_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_WithInvalidToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostBatch_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var content = JsonContent.Create(new { secretNames = new[] { "oauth-steam-api-key" } });
        var response = await client.PostAsync("/api/v1/secrets/batch", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetSecret_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var content = JsonContent.Create(new { value = "new-secret-value" });
        var response = await client.PutAsync("/api/v1/secrets/oauth-steam-api-key", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RotateSecret_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/secrets/oauth-steam-api-key/rotate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSecret_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Authorization Tests - Read

    [Fact]
    public async Task GetSecret_WithReadOAuthPermission_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:read:oauth");

        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_WithFullAdmin_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient("admin-1", "secrets:*");

        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_WithWrongCategoryPermission_Returns403()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:read:infrastructure");

        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_WithNoPermissions_Returns403()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1");

        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOAuthSecrets_WithOAuthPermission_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:read:oauth");

        var response = await client.GetAsync("/api/v1/secrets/oauth");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetInfrastructureSecrets_WithoutPermission_Returns403()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:read:oauth");

        var response = await client.GetAsync("/api/v1/secrets/infrastructure");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Authorization Tests - Write

    [Fact]
    public async Task SetSecret_WithWritePermission_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:write:oauth");

        var content = JsonContent.Create(new { value = "new-secret-value" });
        var response = await client.PutAsync("/api/v1/secrets/oauth-steam-api-key", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetSecret_WithReadOnlyPermission_Returns403()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:read:oauth");

        var content = JsonContent.Create(new { value = "new-secret-value" });
        var response = await client.PutAsync("/api/v1/secrets/oauth-steam-api-key", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSecret_WithDeletePermission_Returns204()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:delete:oauth");

        var response = await client.DeleteAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSecret_WithReadPermission_Returns403()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:read:oauth");

        var response = await client.DeleteAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Authorization Tests - Rotate

    [Fact]
    public async Task RotateSecret_WithRotatePermission_Returns200()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:rotate:oauth");

        var response = await client.PostAsync("/api/v1/secrets/oauth-steam-api-key/rotate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RotateSecret_WithWritePermission_Returns403()
    {
        // Write permission alone doesn't grant rotate
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:write:oauth");

        var response = await client.PostAsync("/api/v1/secrets/oauth-steam-api-key/rotate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Input Validation Tests

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("secret';DROP TABLE--")]
    [InlineData("<script>alert(1)</script>")]
    // Note: Null byte test ("secret\0name") removed - ASP.NET Core rejects at framework level
    public async Task GetSecret_WithMaliciousSecretName_Returns400(string maliciousName)
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        var response = await client.GetAsync($"/api/v1/secrets/{Uri.EscapeDataString(maliciousName)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_WithEmptyName_Returns404OrBadRequest()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        // Empty route segment typically results in 404 (route not matched) or 400
        var response = await client.GetAsync("/api/v1/secrets/");

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 404 or 400, got {response.StatusCode}");
    }

    [Fact]
    public async Task SetSecret_WithEmptyValue_Returns400()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:write:oauth");

        var content = JsonContent.Create(new { value = "" });
        var response = await client.PutAsync("/api/v1/secrets/oauth-steam-api-key", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetSecret_WithNullValue_Returns400()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:write:oauth");

        var content = JsonContent.Create(new { value = (string?)null });
        var response = await client.PutAsync("/api/v1/secrets/oauth-steam-api-key", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Break-Glass Access Tests

    [Fact]
    public async Task GetSecret_WithBreakGlass_BypassesNormalAuthorization()
    {
        using var client = _factory.CreateBreakGlassClient("emergency-user", "Critical incident");

        // No permission claims, but break-glass should allow access
        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetSecret_WithBreakGlass_BypassesNormalAuthorization()
    {
        using var client = _factory.CreateBreakGlassClient("emergency-user", "Critical incident");

        var content = JsonContent.Create(new { value = "emergency-value" });
        var response = await client.PutAsync("/api/v1/secrets/oauth-steam-api-key", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Service Account Tests

    [Fact]
    public async Task GetSecret_AsServiceAccount_DetectsServiceAccountType()
    {
        using var client = _factory.CreateServiceAccountClient("svc-betterauth", "secrets:read:oauth");

        var response = await client.GetAsync("/api/v1/secrets/oauth-steam-api-key");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Secret Not in Allowed List

    [Fact]
    public async Task GetSecret_NotInAllowedList_Returns403()
    {
        using var client = _factory.CreateAuthenticatedClient("user-1", "secrets:*");

        // This secret is not in the AllowedSecrets list
        var response = await client.GetAsync("/api/v1/secrets/some-unknown-secret");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion
}

public sealed class SecureSecretsWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSigningKey = "this-is-a-test-signing-key-for-jwt-tokens-minimum-256-bits";
    private const string TestIssuer = "https://test-issuer.local";
    private const string TestAudience = "test-api";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Secrets:KeyVaultUri"] = "https://example.vault.azure.net/",
                ["Secrets:AllowedSecrets:OAuth:0"] = "oauth-steam-api-key",
                ["Secrets:AllowedSecrets:OAuth:1"] = "oauth-discord-client-secret",
                ["Secrets:AllowedSecrets:BetterAuth:0"] = "betterauth-jwt-secret",
                ["Secrets:AllowedSecrets:Infrastructure:0"] = "infra-db-password",
                ["Auth:Issuer"] = TestIssuer,
                ["Auth:Audience"] = TestAudience,
                ["Auth:SigningKey"] = TestSigningKey,
                ["Readiness:ProbeSecretName"] = "oauth-steam-api-key",
                ["Readiness:CheckCertificates"] = "false"
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace real providers with fakes
            services.RemoveAll<ISecretProvider>();
            services.AddSingleton<ISecretProvider>(new FakeSecretProvider());

            services.RemoveAll<ICertificateProvider>();
            services.AddSingleton<ICertificateProvider>(new FakeCertificateProvider());

            // Configure test JWT authentication
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(string userId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("principal_type", "user")
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        return CreateClientWithToken(claims);
    }

    public HttpClient CreateServiceAccountClient(string serviceId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", serviceId),
            new("principal_type", "service")
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        return CreateClientWithToken(claims);
    }

    public HttpClient CreateBreakGlassClient(string userId, string reason)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("principal_type", "user"),
            new("break_glass", "true"),
            new("break_glass_reason", reason)
        };

        return CreateClientWithToken(claims);
    }

    private HttpClient CreateClientWithToken(List<Claim> claims)
    {
        var token = GenerateJwtToken(claims);
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string GenerateJwtToken(List<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FakeSecretProvider : ISecretProvider
    {
        private readonly Dictionary<string, string> _secrets = new()
        {
            ["oauth-steam-api-key"] = "fake-steam-key",
            ["oauth-discord-client-secret"] = "fake-discord-secret",
            ["betterauth-jwt-secret"] = "fake-jwt-secret",
            ["infra-db-password"] = "fake-db-password"
        };

        private readonly HashSet<string> _allowedSecrets = new(StringComparer.OrdinalIgnoreCase)
        {
            "oauth-steam-api-key",
            "oauth-discord-client-secret",
            "betterauth-jwt-secret",
            "infra-db-password"
        };

        public Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
        {
            _secrets.TryGetValue(secretName, out var value);
            return Task.FromResult(value);
        }

        public Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default)
        {
            var result = new Dictionary<string, string>();
            foreach (var name in secretNames)
            {
                if (_secrets.TryGetValue(name, out var value))
                {
                    result[name] = value;
                }
            }
            return Task.FromResult(result);
        }

        public bool IsAllowed(string secretName) => _allowedSecrets.Contains(secretName);

        public Task<bool> SetSecretAsync(string secretName, string value, CancellationToken ct = default)
        {
            _secrets[secretName] = value;
            return Task.FromResult(true);
        }

        public Task<(string Version, DateTime CreatedAt)> RotateSecretAsync(string secretName, CancellationToken ct = default)
        {
            _secrets[secretName] = Guid.NewGuid().ToString("N");
            return Task.FromResult((Guid.NewGuid().ToString("N"), DateTime.UtcNow));
        }

        public Task<bool> DeleteSecretAsync(string secretName, CancellationToken ct = default)
        {
            return Task.FromResult(_secrets.Remove(secretName));
        }
    }

    private sealed class FakeCertificateProvider : ICertificateProvider
    {
        public Task<List<CertificateInfo>> ListCertificatesAsync(string? vaultName = null, CancellationToken ct = default)
        {
            return Task.FromResult(new List<CertificateInfo>());
        }

        public Task<ImportCertificateResult> ImportCertificateAsync(string name, byte[] certificateData, string? password = null, string? vaultName = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteCertificateAsync(string name, string? vaultName = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }
}
