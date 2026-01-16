using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using StackExchange.Redis;

namespace Dhadgar.Identity.Tests;

public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"identity-tests-{Guid.NewGuid()}";
    public TestIdentityEventPublisher EventPublisher { get; } = new();
    private static readonly ExchangeTokenKeyMaterial ExchangeTokenKey = CreateExchangeTokenKeyMaterial();

    public static ECDsa CreateExchangeTokenKey()
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(ExchangeTokenKey.PrivateKeyPem);
        return ecdsa;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["OAuth:Steam:ApplicationKey"] = "mock",
                ["OAuth:BattleNet:ClientId"] = "mock",
                ["OAuth:BattleNet:ClientSecret"] = "mock",
                ["OAuth:BattleNet:Region"] = "America",
                ["OAuth:Epic:ClientId"] = "mock",
                ["OAuth:Epic:ClientSecret"] = "mock",
                ["OAuth:Epic:AuthorizationEndpoint"] = "https://example.test/epic/authorize",
                ["OAuth:Epic:TokenEndpoint"] = "https://example.test/epic/token",
                ["OAuth:Epic:UserInformationEndpoint"] = "https://example.test/epic/userinfo",
                ["OAuth:Xbox:ClientId"] = "mock",
                ["OAuth:Xbox:ClientSecret"] = "mock",
                ["OAuth:AllowedRedirectHosts:0"] = "panel.meridianconsole.com",
                ["OAuth:AllowedRedirectHosts:1"] = "meridianconsole.com",
                ["Auth:Exchange:PublicKeyPem"] = ExchangeTokenKey.PublicKeyPem,
                ["Auth:KeyVault:VaultUri"] = string.Empty,
                ["Auth:KeyVault:ExchangePublicKeyName"] = string.Empty
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            var efProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<IdentityDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(efProvider));

            services.RemoveAll<IConnectionMultiplexer>();
            services.RemoveAll<IExchangeTokenReplayStore>();

            services.AddSingleton<IExchangeTokenReplayStore>(new InMemoryExchangeTokenReplayStore());

            services.RemoveAll<IIdentityEventPublisher>();
            services.AddSingleton<IIdentityEventPublisher>(EventPublisher);

            services.RemoveAll<IWebhookSecretProvider>();
            services.AddSingleton<IWebhookSecretProvider>(new TestWebhookSecretProvider());

            // Configure test authentication that satisfies authorization policies
            // Remove the OpenIddict validation handler registration and replace with test handler
            services.RemoveAll<IAuthenticationSchemeProvider>();
            services.AddSingleton<IAuthenticationSchemeProvider, TestAuthenticationSchemeProvider>();

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>
    /// Creates an HTTP client with test authentication configured for the specified user.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(Guid userId, Guid? organizationId = null, string? role = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        if (organizationId.HasValue)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.OrgIdHeader, organizationId.Value.ToString());
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        }
        return client;
    }

    /// <summary>
    /// Creates an HTTP request message with test authentication for the specified user.
    /// </summary>
    public static void AddTestAuth(HttpRequestMessage request, Guid userId, Guid? organizationId = null, string? role = null)
    {
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        if (organizationId.HasValue)
        {
            request.Headers.Add(TestAuthHandler.OrgIdHeader, organizationId.Value.ToString());
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            request.Headers.Add(TestAuthHandler.RoleHeader, role);
        }
    }

    public async Task<Guid> SeedUserAsync(string email = "user@example.com")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalAuthId = Guid.NewGuid().ToString("N"),
            Email = email,
            EmailVerified = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private sealed class TestWebhookSecretProvider : IWebhookSecretProvider
    {
        public Task<string?> GetBetterAuthSecretAsync(CancellationToken ct = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class InMemoryExchangeTokenReplayStore : IExchangeTokenReplayStore
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new();

        public Task<bool> MarkAsUsedAsync(string jti, TimeSpan ttl, CancellationToken ct = default)
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
            return Task.FromResult(_entries.TryAdd(jti, expiresAt));
        }
    }

    private sealed record ExchangeTokenKeyMaterial(string PrivateKeyPem, string PublicKeyPem);

    private static ExchangeTokenKeyMaterial CreateExchangeTokenKeyMaterial()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        var privatePem = new string(PemEncoding.Write("PRIVATE KEY", privateKey));
        var publicPem = new string(PemEncoding.Write("PUBLIC KEY", publicKey));
        return new ExchangeTokenKeyMaterial(privatePem, publicPem);
    }
}

/// <summary>
/// Custom authentication scheme provider for tests.
/// Maps the OpenIddict validation scheme to the test handler while preserving other schemes.
/// </summary>
public sealed class TestAuthenticationSchemeProvider : AuthenticationSchemeProvider
{
    private readonly AuthenticationScheme _testScheme;

    public TestAuthenticationSchemeProvider(IOptions<AuthenticationOptions> options)
        : base(options)
    {
        _testScheme = new AuthenticationScheme(
            TestAuthHandler.SchemeName,
            TestAuthHandler.SchemeName,
            typeof(TestAuthHandler));
    }

    public override Task<AuthenticationScheme?> GetSchemeAsync(string name)
    {
        // Only override the Bearer/OpenIddict validation scheme
        // Let other schemes (like OpenIddict Server) work normally
        if (name == TestAuthHandler.SchemeName)
        {
            return Task.FromResult<AuthenticationScheme?>(_testScheme);
        }

        return base.GetSchemeAsync(name);
    }

    public override Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        return Task.FromResult<AuthenticationScheme?>(_testScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        return Task.FromResult<AuthenticationScheme?>(_testScheme);
    }
}

/// <summary>
/// Test authentication handler for integration tests.
/// Reads user identity from test headers instead of JWT validation.
/// Uses the OpenIddict scheme name to satisfy authorization policies.
/// SECURITY: This is ONLY for testing - never use in production.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Use the OpenIddict scheme name so authorization policies that require Bearer scheme are satisfied.
    /// </summary>
    public static readonly string SchemeName = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    public const string UserIdHeader = "X-Test-User-Id";
    public const string OrgIdHeader = "X-Test-Org-Id";
    public const string RoleHeader = "X-Test-Role";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for test user ID header
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userIdValues) ||
            !Guid.TryParse(userIdValues.FirstOrDefault(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString())
        };

        // Add organization if provided
        if (Request.Headers.TryGetValue(OrgIdHeader, out var orgIdValues) &&
            Guid.TryParse(orgIdValues.FirstOrDefault(), out var orgId))
        {
            claims.Add(new Claim("org_id", orgId.ToString()));
        }

        // Add role if provided
        if (Request.Headers.TryGetValue(RoleHeader, out var roleValues) &&
            !string.IsNullOrWhiteSpace(roleValues.FirstOrDefault()))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleValues.First()!));
            claims.Add(new Claim("role", roleValues.First()!));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
