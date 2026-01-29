using Dhadgar.Servers.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dhadgar.Servers.Tests;

/// <summary>
/// WebApplicationFactory for Servers service integration tests.
/// Uses InMemory database with isolated provider to avoid conflicts with Npgsql.
/// </summary>
public class ServersWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"servers-tests-{Guid.NewGuid()}";

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

            // Register TimeProvider for AuditCleanupService
            services.AddSingleton(TimeProvider.System);
        });
    }
}
