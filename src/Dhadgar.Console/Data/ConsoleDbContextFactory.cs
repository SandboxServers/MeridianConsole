using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Console.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// </summary>
public sealed class ConsoleDbContextFactory : IDesignTimeDbContextFactory<ConsoleDbContext>
{
    public ConsoleDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? Environment.GetEnvironmentVariable("DHADGAR_CONSOLE_CONNECTION")
            ?? throw new InvalidOperationException(
                "Database connection string not configured. " +
                "Set 'ConnectionStrings:Postgres' in appsettings.json or DHADGAR_CONSOLE_CONNECTION environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<ConsoleDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ConsoleDbContext(optionsBuilder.Options);
    }
}
