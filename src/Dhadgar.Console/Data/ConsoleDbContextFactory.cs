using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dhadgar.Console.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// </summary>
public sealed class ConsoleDbContextFactory : IDesignTimeDbContextFactory<ConsoleDbContext>
{
    public ConsoleDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConsoleDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=dhadgar_console;Username=dhadgar;Password=dhadgar");

        return new ConsoleDbContext(optionsBuilder.Options);
    }
}
