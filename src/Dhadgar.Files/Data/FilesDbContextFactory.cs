using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dhadgar.Files.Data;

// Enables `dotnet ef` without needing the service running.
public sealed class FilesDbContextFactory : IDesignTimeDbContextFactory<FilesDbContext>
{
    public FilesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FilesDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar")
            .Options;

        return new FilesDbContext(options);
    }
}
