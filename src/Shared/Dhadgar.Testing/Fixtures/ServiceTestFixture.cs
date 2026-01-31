using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dhadgar.Testing.Fixtures;

/// <summary>
/// Base test fixture for integration testing of services with Entity Framework Core.
/// Provides a WebApplicationFactory with an in-memory database for test isolation.
/// </summary>
/// <typeparam name="TProgram">The service's Program class (must have public partial Program)</typeparam>
/// <typeparam name="TDbContext">The service's DbContext class</typeparam>
public abstract class ServiceTestFixture<TProgram, TDbContext> : WebApplicationFactory<TProgram>, IAsyncLifetime
    where TProgram : class
    where TDbContext : DbContext
{
    private readonly string _databaseName;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceTestFixture{TProgram, TDbContext}"/> class.
    /// Creates a unique in-memory database name for test isolation.
    /// </summary>
    protected ServiceTestFixture()
    {
        _databaseName = $"TestDb_{Guid.NewGuid()}";
    }

    /// <summary>
    /// Gets the name of the in-memory database used for this test fixture.
    /// </summary>
    protected string DatabaseName => _databaseName;

    /// <summary>
    /// Configures the web host to use an in-memory database for testing.
    /// </summary>
    /// <param name="builder">The web host builder to configure</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove all DbContext registrations (handles both AddDbContext and AddDbContextPool)
            services.RemoveAll<DbContextOptions<TDbContext>>();
            services.RemoveAll<TDbContext>();

            // Add in-memory database with unique name for isolation
            services.AddDbContext<TDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Call virtual method to allow derived classes to customize services
            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// Override this method to add custom service configuration for tests.
    /// Called after the in-memory database is configured.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Default implementation does nothing - override in derived classes
    }

    /// <summary>
    /// Resets the in-memory database by clearing all data.
    /// Call this between tests for complete isolation.
    /// </summary>
    protected async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // Clear all data from the database
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Initializes the test fixture asynchronously.
    /// </summary>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes of the test fixture asynchronously.
    /// </summary>
    Task IAsyncLifetime.DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
