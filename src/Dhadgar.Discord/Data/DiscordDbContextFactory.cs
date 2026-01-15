using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dhadgar.Discord.Data;

/// <summary>
/// Design-time factory for EF Core CLI commands (migrations, etc.).
/// </summary>
public sealed class DiscordDbContextFactory : IDesignTimeDbContextFactory<DiscordDbContext>
{
    public DiscordDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DiscordDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar");
        return new DiscordDbContext(optionsBuilder.Options);
    }
}
