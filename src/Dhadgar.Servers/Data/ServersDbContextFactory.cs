using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dhadgar.Servers.Data;

// Enables `dotnet ef` without needing the service running.
public sealed class ServersDbContextFactory : IDesignTimeDbContextFactory<ServersDbContext>
{
    public ServersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ServersDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar")
            .Options;

        return new ServersDbContext(options);
    }
}
