using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dhadgar.Mods.Data;

// Enables `dotnet ef` without needing the service running.
public sealed class ModsDbContextFactory : IDesignTimeDbContextFactory<ModsDbContext>
{
    public ModsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ModsDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar")
            .Options;

        return new ModsDbContext(options);
    }
}
