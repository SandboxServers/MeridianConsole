using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dhadgar.Billing.Data;

// Enables `dotnet ef` without needing the service running.
public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar")
            .Options;

        return new BillingDbContext(options);
    }
}
