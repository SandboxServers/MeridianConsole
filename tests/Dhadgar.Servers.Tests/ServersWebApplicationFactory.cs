using Dhadgar.Servers.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Servers.Tests;

/// <summary>
/// WebApplicationFactory for Servers service integration tests.
/// Uses InMemory database with isolated provider to avoid conflicts with Npgsql.
/// </summary>
public class ServersWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"servers-tests-{Guid.NewGuid()}";

    /// <summary>
    /// Test-friendly TimeProvider that can be controlled in tests.
    /// </summary>
    public FakeTimeProvider FakeTimeProvider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove all DbContext-related registrations to avoid provider conflicts
            services.RemoveAll<DbContextOptions<ServersDbContext>>();

            // Create isolated EF provider for InMemory
            var efProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            // Add InMemory database for testing with isolated provider
            services.AddDbContext<ServersDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(efProvider));

            // Register FakeTimeProvider for testing time-dependent services
            services.AddSingleton<TimeProvider>(FakeTimeProvider);
        });
    }
}
