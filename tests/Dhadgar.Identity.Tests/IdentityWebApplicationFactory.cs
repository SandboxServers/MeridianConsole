using System.Collections.Concurrent;
using System.Security.Cryptography;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        ecdsa.ImportParameters(ExchangeTokenKey.Parameters);
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
        });
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

    private sealed record ExchangeTokenKeyMaterial(ECParameters Parameters, string PublicKeyPem);

    private static ExchangeTokenKeyMaterial CreateExchangeTokenKeyMaterial()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(true);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        var pem = new string(PemEncoding.Write("PUBLIC KEY", publicKey));
        return new ExchangeTokenKeyMaterial(parameters, pem);
    }

}
