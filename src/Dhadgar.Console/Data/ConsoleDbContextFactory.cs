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
            ?? "Host=localhost;Database=dhadgar_console;Username=dhadgar;Password=dhadgar";

        var optionsBuilder = new DbContextOptionsBuilder<ConsoleDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ConsoleDbContext(optionsBuilder.Options);
    }
}
