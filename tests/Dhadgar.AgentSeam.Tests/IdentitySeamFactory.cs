extern alias identity;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using identity::Dhadgar.Identity.Data;
using identity::Dhadgar.Identity.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using StackExchange.Redis;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Runs the real Identity host (in the Testing environment) for the client_credentials seam (PI1).
/// Only the infrastructure is replaced: an in-memory database, the exchange replay store, the event publisher
/// and the webhook secret provider. The token endpoint keeps OpenIddict server auth, so the scheme provider is left alone.
/// </summary>
public sealed class IdentitySeamFactory : WebApplicationFactory<identity::Program>
{
    private const string Issuer = "https://seams.test/identity-real";
    private const string Audience = "meridian-api";

    private readonly string _databaseName = $"identity-seam-{Guid.NewGuid()}";
    private readonly string _exchangePublicKeyPem = CreateExchangePublicKeyPem();

    /// <summary>The issuer Identity stamps on tokens (also what the discovery document reports).</summary>
    public static string TokenIssuer => Issuer;

    /// <summary>The audience Identity stamps on default-audience tokens.</summary>
    public static string TokenAudience => Audience;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = Issuer,
                ["Auth:Audience"] = Audience,
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
                ["Auth:Exchange:PublicKeyPem"] = _exchangePublicKeyPem,
                ["Auth:KeyVault:VaultUri"] = string.Empty,
                ["Auth:KeyVault:ExchangePublicKeyName"] = string.Empty
            });
        });

        builder.ConfigureTestServices(services =>
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
            services.AddSingleton<IExchangeTokenReplayStore>(new AlwaysFreshReplayStore());

            services.RemoveAll<IIdentityEventPublisher>();
            services.AddSingleton(Substitute.For<IIdentityEventPublisher>());

            services.RemoveAll<IWebhookSecretProvider>();
            services.AddSingleton<IWebhookSecretProvider>(new NoWebhookSecretProvider());
        });
    }

    /// <summary>Creates the Identity database schema the OpenIddict stores need. Safe to call more than once.</summary>
    public async Task EnsureDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static string CreateExchangePublicKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new string(PemEncoding.Write("PUBLIC KEY", ecdsa.ExportSubjectPublicKeyInfo()));
    }

    private sealed class AlwaysFreshReplayStore : IExchangeTokenReplayStore
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new();

        public Task<bool> MarkAsUsedAsync(string jti, TimeSpan ttl, CancellationToken ct = default)
        {
            return Task.FromResult(_entries.TryAdd(jti, DateTimeOffset.UtcNow.Add(ttl)));
        }
    }

    private sealed class NoWebhookSecretProvider : IWebhookSecretProvider
    {
        public Task<string?> GetBetterAuthSecretAsync(CancellationToken ct = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
