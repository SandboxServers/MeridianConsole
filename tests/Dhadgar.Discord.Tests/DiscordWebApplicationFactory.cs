using Dhadgar.Discord.Bot;
using Dhadgar.Discord.Commands;
using Dhadgar.Discord.Data;
using Dhadgar.Discord.Services;
using Discord;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Dhadgar.Discord.Tests;

/// <summary>
/// WebApplicationFactory for Discord service tests.
/// Configures in-memory database and mocks external dependencies.
/// </summary>
public class DiscordWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Configure test admin API key
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminApiKey"] = "test-admin-key"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove all database-related registrations completely
            // This includes the DbContext itself, options, and any factory registrations
            var dbDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DiscordDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<DiscordDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ImplementationType == typeof(DiscordDbContext))
                .ToList();
            foreach (var descriptor in dbDescriptors)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database with unique name per test instance
            var databaseName = $"DiscordTestDb_{Guid.NewGuid()}";
            services.AddDbContext<DiscordDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(databaseName);
            }, ServiceLifetime.Scoped, ServiceLifetime.Scoped);

            // Remove ALL hosted services to prevent bot from starting
            // This is needed because the registration uses a factory that depends on DiscordBotService
            var hostedServicesToRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var descriptor in hostedServicesToRemove)
            {
                services.Remove(descriptor);
            }

            // Remove DiscordBotService registrations
            services.RemoveAll<DiscordBotService>();
            services.RemoveAll<IDiscordBotService>();

            // Add mock bot service - return Disconnected since we can't properly mock DiscordSocketClient
            var mockBotService = Substitute.For<IDiscordBotService>();
            mockBotService.ConnectionState.Returns(ConnectionState.Disconnected);
            services.AddSingleton(mockBotService);

            // Remove SlashCommandHandler (depends on bot)
            services.RemoveAll<SlashCommandHandler>();

            // Mock the credential provider (it's an interface so can be mocked)
            services.RemoveAll<IDiscordCredentialProvider>();
            var mockCredentialProvider = Substitute.For<IDiscordCredentialProvider>();
            mockCredentialProvider.GetBotTokenAsync(Arg.Any<CancellationToken>())
                .Returns("test-bot-token");
            mockCredentialProvider.GetClientIdAsync(Arg.Any<CancellationToken>())
                .Returns("test-client-id");
            mockCredentialProvider.GetClientSecretAsync(Arg.Any<CancellationToken>())
                .Returns("test-client-secret");
            services.AddSingleton(mockCredentialProvider);

            // Mock the platform health service
            services.RemoveAll<IPlatformHealthService>();
            var mockHealthService = Substitute.For<IPlatformHealthService>();
            var mockServices = new List<ServiceHealthStatus>
            {
                new("Gateway", "http://localhost:5000", true, 100, null),
                new("Identity", "http://localhost:5010", true, 50, null)
            };
            mockHealthService.CheckAllServicesAsync(Arg.Any<CancellationToken>())
                .Returns(new PlatformHealthStatus(
                    Services: mockServices,
                    HealthyCount: 2,
                    UnhealthyCount: 0,
                    CheckedAtUtc: DateTimeOffset.UtcNow));
            services.AddSingleton(mockHealthService);
        });
    }

    /// <summary>
    /// Gets a scoped service for testing.
    /// Note: Caller is responsible for disposing the returned scope if needed.
    /// </summary>
    public (T Service, IServiceScope Scope) GetScopedService<T>() where T : notnull
    {
        var scope = Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<T>(), scope);
    }

    /// <summary>
    /// Seeds the test database with initial data.
    /// </summary>
    public async Task SeedDatabaseAsync(Func<DiscordDbContext, Task> seedAction)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        await seedAction(db);
    }
}
