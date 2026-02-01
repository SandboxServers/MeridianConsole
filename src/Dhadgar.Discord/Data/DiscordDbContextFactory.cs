using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Discord.Data;

/// <summary>
/// Design-time factory for EF Core CLI commands (migrations, etc.).
/// Loads connection string from configuration files.
/// </summary>
public sealed class DiscordDbContextFactory : IDesignTimeDbContextFactory<DiscordDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=dhadgar_discord;Username=dhadgar;Password=dhadgar";

    public DiscordDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres") ?? FallbackConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<DiscordDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new DiscordDbContext(optionsBuilder.Options);
    }
}
