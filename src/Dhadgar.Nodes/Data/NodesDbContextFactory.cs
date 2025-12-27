using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dhadgar.Nodes.Data;

// Enables `dotnet ef` without needing the service running.
public sealed class NodesDbContextFactory : IDesignTimeDbContextFactory<NodesDbContext>
{
    public NodesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar")
            .Options;

        return new NodesDbContext(options);
    }
}
