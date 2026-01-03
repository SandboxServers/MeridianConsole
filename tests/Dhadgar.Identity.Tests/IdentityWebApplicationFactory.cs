using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dhadgar.Identity.Tests;

public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"identity-tests-{Guid.NewGuid()}";
    public TestIdentityEventPublisher EventPublisher { get; } = new();

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
                ["OAuth:AllowedRedirectHosts:1"] = "meridianconsole.com"
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            var provider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<IdentityDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(provider));

            services.RemoveAll<IIdentityEventPublisher>();
            services.AddSingleton<IIdentityEventPublisher>(EventPublisher);
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
}
