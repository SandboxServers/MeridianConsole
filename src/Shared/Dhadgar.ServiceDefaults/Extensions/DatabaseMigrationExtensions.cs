using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Database.MigrateAsync();
    }
}
