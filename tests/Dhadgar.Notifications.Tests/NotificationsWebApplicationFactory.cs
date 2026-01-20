using Dhadgar.Notifications.Data;
using Dhadgar.Notifications.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Dhadgar.Notifications.Tests;

/// <summary>
/// WebApplicationFactory for Notifications service tests.
/// Configures in-memory database and mocks external dependencies.
/// </summary>
public class NotificationsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<NotificationsDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Remove any DbContext registration
            services.RemoveAll<NotificationsDbContext>();
            services.RemoveAll<DbContextOptions<NotificationsDbContext>>();

            // Add in-memory database with unique name per instance
            services.AddDbContext<NotificationsDbContext>(options =>
            {
                options.UseInMemoryDatabase($"NotificationsTestDb_{Guid.NewGuid()}");
            });

            // Remove ALL hosted services to prevent MassTransit consumers from starting
            var hostedServicesToRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var descriptor in hostedServicesToRemove)
            {
                services.Remove(descriptor);
            }

            // Mock the email provider to prevent actual emails during tests
            services.RemoveAll<IEmailProvider>();
            var mockEmailProvider = Substitute.For<IEmailProvider>();
            mockEmailProvider.SendEmailAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
                .Returns(new EmailSendResult(true));
            mockEmailProvider.SendEmailAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
                .Returns(new EmailSendResult(true));
            services.AddSingleton(mockEmailProvider);
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
    public async Task SeedDatabaseAsync(Func<NotificationsDbContext, Task> seedAction)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await seedAction(db);
    }
}
