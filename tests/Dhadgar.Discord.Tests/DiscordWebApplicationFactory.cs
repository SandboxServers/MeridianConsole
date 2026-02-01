#nullable enable

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
/// Wrapper that owns both a service instance and its scope, ensuring proper disposal.
/// Implements IAsyncDisposable for async test cleanup.
/// </summary>
/// <typeparam name="T">The type of service being wrapped.</typeparam>
public sealed class ScopedServiceWrapper<T> : IAsyncDisposable, IDisposable where T : notnull
{
    private readonly IServiceScope _scope;
    private bool _disposed;

    public ScopedServiceWrapper(T service, IServiceScope scope)
    {
        Service = service;
        _scope = scope;
    }

    /// <summary>
    /// The scoped service instance.
    /// </summary>
    public T Service { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _scope.Dispose();
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

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
            // This is needed because hosted services may have transitive dependencies on DiscordBotService
            // which we mock, and factory-registered services can't be filtered by type name alone
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
                new(serviceName: "Gateway", url: new Uri("http://localhost:5000"), isHealthy: true, responseTimeMs: 100, error: null),
                new(serviceName: "Identity", url: new Uri("http://localhost:5010"), isHealthy: true, responseTimeMs: 50, error: null)
            };
            mockHealthService.CheckAllServicesAsync(Arg.Any<CancellationToken>())
                .Returns(new PlatformHealthStatus(
                    services: mockServices,
                    healthyCount: 2,
                    unhealthyCount: 0,
                    checkedAtUtc: DateTimeOffset.UtcNow));
            services.AddSingleton(mockHealthService);
        });
    }

    /// <summary>
    /// Gets a scoped service for testing wrapped in a disposable container.
    /// The returned wrapper automatically disposes the underlying scope when disposed.
    /// </summary>
    /// <example>
    /// <code>
    /// await using var wrapper = factory.GetScopedService&lt;DiscordDbContext&gt;();
    /// var db = wrapper.Service;
    /// // Use db...
    /// </code>
    /// </example>
    public ScopedServiceWrapper<T> GetScopedService<T>() where T : notnull
    {
        var scope = Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<T>();
        return new ScopedServiceWrapper<T>(service, scope);
    }

    /// <summary>
    /// Seeds the test database with initial data.
    /// Automatically calls SaveChangesAsync after the seed action if there are pending changes.
    /// </summary>
    public async Task SeedDatabaseAsync(Func<DiscordDbContext, Task> seedAction)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        await seedAction(db);

        // Ensure seeded data is persisted if seedAction didn't call SaveChangesAsync
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }
}
