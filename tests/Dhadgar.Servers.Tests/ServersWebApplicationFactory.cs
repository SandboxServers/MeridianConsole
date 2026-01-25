using Dhadgar.Servers.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Servers.Tests;

/// <summary>
/// WebApplicationFactory for Servers service integration tests.
/// Uses SQLite in-memory database to support ExecuteDeleteAsync for audit tests.
/// </summary>
public class ServersWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public ServersWebApplicationFactory()
    {
        // Keep connection open for the lifetime of the factory
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ServersDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove any existing connection factory
            var connectionFactoryDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(Microsoft.EntityFrameworkCore.Storage.RelationalConnectionDependencies));
            if (connectionFactoryDescriptor != null)
            {
                services.Remove(connectionFactoryDescriptor);
            }

            // Add SQLite in-memory database for testing
            // SQLite supports ExecuteDeleteAsync unlike the InMemory provider
            services.AddDbContext<ServersDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Build the service provider and ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServersDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
