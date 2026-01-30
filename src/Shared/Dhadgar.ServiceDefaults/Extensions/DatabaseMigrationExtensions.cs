using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Extensions;

/// <summary>
/// Extension methods for automated database migrations during development.
/// </summary>
public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Automatically applies pending EF Core migrations in Development environment.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type to migrate.</typeparam>
    /// <param name="app">The web application instance.</param>
    /// <returns>A task representing the asynchronous migration operation.</returns>
    /// <remarks>
    /// This method only runs migrations if <see cref="IHostEnvironment.IsDevelopment"/> is true.
    /// In production, migrations should be applied via deployment pipelines or manual intervention.
    /// </remarks>
    public static async Task AutoMigrateDatabaseAsync<TDbContext>(this WebApplication app)
        where TDbContext : DbContext
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseMigrationExtensions));
        var contextName = typeof(TDbContext).Name;

        try
        {
            logger.LogInformation("Applying migrations for {DbContext}...", contextName);
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully for {DbContext}", contextName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply migrations for {DbContext}", contextName);
            throw;
        }
    }
}
